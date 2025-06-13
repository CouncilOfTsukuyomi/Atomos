
using System.Collections.Concurrent;
using System.Threading.Channels;
using CommonLib.Enums;
using CommonLib.Interfaces;
using CommonLib.Models;
using LiteDB;
using NLog;
using PenumbraModForwarder.Statistics.Models;

namespace PenumbraModForwarder.Statistics.Services;

public class StatisticService : IStatisticService, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly string _databasePath;
    private readonly IFileStorage _fileStorage;
    
    // Connection pooling and batching
    private readonly SemaphoreSlim _dbSemaphore = new(1, 1);
    private readonly Channel<DatabaseOperation> _operationChannel;
    private readonly ChannelWriter<DatabaseOperation> _operationWriter;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _backgroundProcessor;
    
    // Caching for read operations
    private readonly ConcurrentDictionary<string, (int value, DateTime lastUpdated)> _statCache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);
    
    // Batching configuration
    private const int BatchSize = 50;
    private const int BatchTimeoutMs = 1000;
    
    private bool _disposed = false;

    public StatisticService(IFileStorage fileStorage, string? databasePath = null)
    {
        _fileStorage = fileStorage;
        
#if DEBUG
        _databasePath = @"C:\Temp\SharedDebugDb\userstats.db";
#else
        _databasePath = databasePath ?? $@"{CommonLib.Consts.ConfigurationConsts.DatabasePath}\userstats.db";
#endif

        // Create bounded channel for better memory management
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        
        _operationChannel = Channel.CreateBounded<DatabaseOperation>(options);
        _operationWriter = _operationChannel.Writer;

        InitializeDatabase();
        
        // Start background processor
        _backgroundProcessor = Task.Run(ProcessOperationsAsync, _cancellationTokenSource.Token);
    }

    private async Task ProcessOperationsAsync()
    {
        var operations = new List<DatabaseOperation>();
        
        await foreach (var operation in _operationChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
        {
            operations.Add(operation);
            
            // Process batch when we reach batch size or timeout
            if (operations.Count >= BatchSize || 
                (operations.Count > 0 && (DateTime.UtcNow - operations[0].Timestamp).TotalMilliseconds >= BatchTimeoutMs))
            {
                await ProcessBatchAsync(operations);
                operations.Clear();
            }
        }
        
        // Process remaining operations
        if (operations.Count > 0)
        {
            await ProcessBatchAsync(operations);
        }
    }

    private async Task ProcessBatchAsync(List<DatabaseOperation> operations)
    {
        if (operations.Count == 0) return;

        await _dbSemaphore.WaitAsync(_cancellationTokenSource.Token);
        try
        {
            // Use connection string with shared mode
            var connectionString = $"Filename={_databasePath};Mode=Shared;";
            using var database = new LiteDatabase(connectionString);
            
            try
            {
                var statsCollection = database.GetCollection<StatRecord>("stats");
                var modsCollection = database.GetCollection<ModInstallationRecord>("mod_installations");
                
                // Group operations by type for better performance
                var statOps = operations.Where(op => op.Type == OperationType.IncrementStat).ToList();
                var modOps = operations.Where(op => op.Type == OperationType.RecordModInstallation).ToList();
                
                // Process stat operations in batch
                ProcessStatOperations(statsCollection, statOps);
                
                // Process mod installation operations in batch
                ProcessModOperations(modsCollection, modOps);
                
                _logger.Debug("Processed batch of {Count} operations successfully", operations.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to process batch operations");
                
                // Re-queue failed operations with exponential backoff
                foreach (var op in operations)
                {
                    op.RetryCount++;
                    if (op.RetryCount <= 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, op.RetryCount)), _cancellationTokenSource.Token);
                        await _operationWriter.WriteAsync(op, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        _logger.Error("Operation failed after 3 retries: {Operation}", op.Type);
                    }
                }
            }
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }


    private void ProcessStatOperations(ILiteCollection<StatRecord> collection, List<DatabaseOperation> operations)
    {
        if (operations.Count == 0) return;
        
        // Group by stat name for batching
        var statGroups = operations.GroupBy(op => op.StatName);
        
        foreach (var group in statGroups)
        {
            var statName = group.Key;
            var incrementCount = group.Count();
            
            var existingRecord = collection.FindOne(x => x.Name == statName);
            
            if (existingRecord == null)
            {
                var newRecord = new StatRecord
                {
                    Name = statName,
                    Count = incrementCount
                };
                collection.Insert(newRecord);
                
                // Update cache
                _statCache[statName] = (incrementCount, DateTime.UtcNow);
            }
            else
            {
                existingRecord.Count += incrementCount;
                collection.Update(existingRecord);
                
                // Update cache
                _statCache[statName] = (existingRecord.Count, DateTime.UtcNow);
            }
            
            _logger.Debug("Processed {Count} increment operations for stat '{Stat}'", incrementCount, statName);
        }
    }

    private void ProcessModOperations(ILiteCollection<ModInstallationRecord> collection, List<DatabaseOperation> operations)
    {
        if (operations.Count == 0) return;
        
        var records = operations.Select(op => new ModInstallationRecord
        {
            ModName = op.ModName,
            InstallationTime = op.Timestamp
        }).ToList();
        
        collection.InsertBulk(records);
        
        _logger.Debug("Processed {Count} mod installation operations", records.Count);
    }



    public async Task IncrementStatAsync(Stat stat)
    {
        var operation = new DatabaseOperation
        {
            Type = OperationType.IncrementStat,
            StatName = stat.ToString(),
            Timestamp = DateTime.UtcNow
        };

        if (!_operationWriter.TryWrite(operation))
        {
            await _operationWriter.WriteAsync(operation, _cancellationTokenSource.Token);
        }
        
        _statCache.AddOrUpdate(stat.ToString(), 
            (1, DateTime.UtcNow),
            (key, existing) => 
            {
                var (value, lastUpdated) = existing;
                return (value + 1, DateTime.UtcNow);
            });
    }

    public async Task<int> GetStatCountAsync(Stat stat)
    {
        var statName = stat.ToString();
        
        // Check cache first
        if (_statCache.TryGetValue(statName, out var cached) && 
            DateTime.UtcNow - cached.lastUpdated < _cacheExpiry)
        {
            return cached.value;
        }

        // Fallback to database
        return await ExecuteDatabaseActionAsync(db =>
        {
            var stats = db.GetCollection<StatRecord>("stats");
            var statRecord = stats.FindOne(x => x.Name == statName);
            
            var count = statRecord?.Count ?? 0;
            
            // Update cache
            _statCache[statName] = (count, DateTime.UtcNow);
            
            return Task.FromResult(count);
        }, $"Failed to retrieve statistic '{stat}'", 0);
    }

    public async Task<int> GetModsInstalledTodayAsync()
    {
        return await ExecuteDatabaseActionAsync(db =>
        {
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            var startOfToday = DateTime.UtcNow.Date;
            
            return Task.FromResult(modInstallations.Count(x => x.InstallationTime >= startOfToday));
        }, "Failed to retrieve mods installed today", 0);
    }

    public async Task RecordModInstallationAsync(string modName)
    {
        var operation = new DatabaseOperation
        {
            Type = OperationType.RecordModInstallation,
            ModName = modName,
            Timestamp = DateTime.UtcNow
        };

        if (!_operationWriter.TryWrite(operation))
        {
            await _operationWriter.WriteAsync(operation, _cancellationTokenSource.Token);
        }

        await IncrementStatAsync(Stat.ModsInstalled);
    }

    public async Task<ModInstallationRecord?> GetMostRecentModInstallationAsync()
    {
        return await ExecuteDatabaseActionAsync(db =>
        {
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            var result = modInstallations
                .FindAll()
                .OrderByDescending(x => x.InstallationTime)
                .FirstOrDefault();
            
            return Task.FromResult(result);
        }, "Failed to retrieve the most recent mod installation");
    }

    public async Task<int> GetUniqueModsInstalledCountAsync()
    {
        return await ExecuteDatabaseActionAsync(db =>
        {
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            var uniqueCount = modInstallations
                .FindAll()
                .Select(x => x.ModName)
                .Distinct()
                .Count();
            
            return Task.FromResult(uniqueCount);
        }, "Failed to retrieve count of unique mods installed", 0);
    }

    public async Task<List<ModInstallationRecord>> GetAllInstalledModsAsync()
    {
        return await ExecuteDatabaseActionAsync(db =>
        {
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            var mods = modInstallations
                .FindAll()
                .OrderByDescending(x => x.InstallationTime)
                .ToList();
            
            return Task.FromResult(mods);
        }, "Failed to retrieve all installed mods", new List<ModInstallationRecord>());
    }

    private async Task<TResult> ExecuteDatabaseActionAsync<TResult>(
        Func<LiteDatabase, Task<TResult>> action,
        string errorContext,
        TResult defaultValue = default)
    {
        await _dbSemaphore.WaitAsync(_cancellationTokenSource.Token);
        try
        {
            // Use connection string with shared mode
            var connectionString = $"Filename={_databasePath};Mode=Shared;";
            using var database = new LiteDatabase(connectionString);
            return await action(database);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, errorContext);
            return defaultValue!;
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }


    private void InitializeDatabase()
    {
        var directoryPath = Path.GetDirectoryName(_databasePath);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            _logger.Info("Created missing directory for database at '{DirectoryPath}'", directoryPath);
        }

        // Use connection string with shared mode
        var connectionString = $"Filename={_databasePath};Mode=Shared;";
    
        using var db = new LiteDatabase(connectionString);
    
        // Ensure collections exist and create indexes
        var stats = db.GetCollection<StatRecord>("stats");
        stats.EnsureIndex(x => x.Name, true); // Unique index on Name
    
        var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
        modInstallations.EnsureIndex(x => x.ModName);
        modInstallations.EnsureIndex(x => x.InstallationTime);
    
        _logger.Info("Database initialized successfully");
    }


    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        // Signal shutdown and wait for background processor to complete
        _operationWriter.Complete();
        _cancellationTokenSource.Cancel();
        
        try
        {
            _backgroundProcessor.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Background processor did not complete gracefully");
        }
        
        _dbSemaphore?.Dispose();
        _cancellationTokenSource?.Dispose();
        
        _logger.Info("StatisticService disposed successfully");
    }

    private class DatabaseOperation
    {
        public OperationType Type { get; set; }
        public string? StatName { get; set; }
        public string? ModName { get; set; }
        public DateTime Timestamp { get; set; }
        public int RetryCount { get; set; }
    }

    private enum OperationType
    {
        IncrementStat,
        RecordModInstallation
    }
}