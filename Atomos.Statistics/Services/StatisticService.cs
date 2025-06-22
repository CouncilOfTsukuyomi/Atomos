
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Atomos.Statistics.Models;
using CommonLib.Enums;
using CommonLib.Interfaces;
using CommonLib.Models;
using LiteDB;
using NLog;

namespace Atomos.Statistics.Services;

public class StatisticService : IStatisticService, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly string _databasePath;
    private readonly IFileStorage _fileStorage;
    
    // In-process synchronization only - let LiteDB handle cross-process
    private static readonly SemaphoreSlim _globalDbSemaphore = new(1, 1);
    
    // Connection pooling and batching
    private readonly Channel<DatabaseOperation> _operationChannel;
    private readonly ChannelWriter<DatabaseOperation> _operationWriter;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _backgroundProcessor;
    
    // Caching for read operations
    private readonly ConcurrentDictionary<string, (int value, DateTime lastUpdated)> _statCache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);
    
    // Batching configuration
    private const int BatchSize = 50;
    private const int BatchTimeoutMs = 100;
    
    // Database initialization tracking
    private readonly TaskCompletionSource<bool> _databaseInitialized = new();
    
    private bool _disposed = false;

    public StatisticService(IFileStorage fileStorage, string? databasePath = null)
    {
        _fileStorage = fileStorage;
        
#if DEBUG
        _databasePath = @"C:\Temp\SharedDebugDb\userstats.db";
#else
        _databasePath = databasePath ?? $@"{CommonLib.Consts.ConfigurationConsts.DatabasePath}\userstats.db";
#endif

        _logger.Info("StatisticService initializing with database path: {DatabasePath}", _databasePath);

        // Create bounded channel for better memory management
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        
        _operationChannel = Channel.CreateBounded<DatabaseOperation>(options);
        _operationWriter = _operationChannel.Writer;

        // Start async initialization - don't block constructor
        _ = Task.Run(async () =>
        {
            try
            {
                await InitializeDatabaseAsync();
                _databaseInitialized.SetResult(true);
                _logger.Info("Database initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Database initialization failed");
                _databaseInitialized.SetException(ex);
            }
        });
        
        // Start background processor
        _backgroundProcessor = Task.Run(ProcessOperationsAsync, _cancellationTokenSource.Token);
        _logger.Info("StatisticService background processor started");
    }

    /// <summary>
    /// Refreshes the cache by clearing all cached statistics, forcing fresh database reads
    /// </summary>
    public async Task RefreshCacheAsync()
    {
        _logger.Debug("RefreshCacheAsync called - invalidating all cached statistics");
        
        // Clear the cache to force fresh database reads
        _statCache.Clear();
        
        _logger.Debug("Cache cleared - next stat reads will come from database");
        
        // Give any pending operations a moment to complete
        await Task.Delay(50);
    }

    /// <summary>
    /// Forces a flush of pending operations and refreshes cache
    /// </summary>
    public async Task FlushAndRefreshAsync(TimeSpan? timeout = null)
    {
        var waitTime = timeout ?? TimeSpan.FromSeconds(5);
        _logger.Debug("FlushAndRefreshAsync called with timeout: {Timeout}", waitTime);
        
        // Wait for pending operations to be processed
        var deadline = DateTime.UtcNow.Add(waitTime);
        while (DateTime.UtcNow < deadline)
        {
            if (_operationChannel.Reader.Count == 0)
            {
                _logger.Debug("All pending operations processed");
                break;
            }
            
            await Task.Delay(50, _cancellationTokenSource.Token);
        }
        
        // Clear cache to ensure fresh reads
        await RefreshCacheAsync();
    }

    private async Task ProcessOperationsAsync()
    {
        _logger.Info("Background processor started - waiting for database initialization");
        
        // Wait for database to be initialized before processing operations
        try
        {
            await _databaseInitialized.Task;
            _logger.Info("Database initialized, background processor now active");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Database initialization failed, background processor will exit");
            return;
        }
        
        var operations = new List<DatabaseOperation>();
        var reader = _operationChannel.Reader;
        
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                var hasMore = await reader.WaitToReadAsync(_cancellationTokenSource.Token);
                if (!hasMore) break; // Channel completed
            
                // Read all available operations
                while (reader.TryRead(out var operation))
                {
                    _logger.Debug("Received operation: {Type} for {Item} at {Timestamp}", 
                        operation.Type, operation.ModName ?? operation.StatName, operation.Timestamp);
                
                    operations.Add(operation);
                }
            
                if (operations.Count == 0) continue;
            
                var timeSinceFirstOperation = (DateTime.UtcNow - operations[0].Timestamp).TotalMilliseconds;
            
                _logger.Debug("Current batch: {Count} operations, time since first: {TimeSinceFirst}ms, batch size limit: {BatchSize}, timeout: {Timeout}ms", 
                    operations.Count, timeSinceFirstOperation, BatchSize, BatchTimeoutMs);
            
                // Process batch when we reach batch size or timeout
                if (operations.Count >= BatchSize || timeSinceFirstOperation >= BatchTimeoutMs)
                {
                    _logger.Info("Processing batch of {Count} operations (trigger: {Trigger})", 
                        operations.Count, 
                        operations.Count >= BatchSize ? "size" : "timeout");
                
                    await ProcessBatchAsync(operations);
                    operations.Clear();
                    _logger.Debug("Batch processed and cleared, waiting for next operations");
                }
                else
                {
                    // Wait for the remaining timeout period before checking again
                    var remainingTimeout = BatchTimeoutMs - (int)timeSinceFirstOperation;
                    if (remainingTimeout > 0)
                    {
                        await Task.Delay(remainingTimeout, _cancellationTokenSource.Token);
                    }
                }
            }
        
            // Process remaining operations
            if (operations.Count > 0)
            {
                _logger.Info("Processing final batch of {Count} operations on shutdown", operations.Count);
                await ProcessBatchAsync(operations);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Background processor cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Critical error in background processor");
        }
        finally
        {
            _logger.Info("Background processor ended");
        }
    }

    private async Task ProcessBatchAsync(List<DatabaseOperation> operations)
    {
        if (operations.Count == 0)
        {
            _logger.Debug("ProcessBatchAsync called with empty operations list");
            return;
        }

        _logger.Info("Starting to process batch of {Count} operations", operations.Count);

        await ExecuteDatabaseActionAsync<bool>(async db =>
        {
            try
            {
                var statsCollection = db.GetCollection<StatRecord>("stats");
                var modsCollection = db.GetCollection<ModInstallationRecord>("mod_installations");
                
                // Group operations by type for better performance
                var statOps = operations.Where(op => op.Type == OperationType.IncrementStat).ToList();
                var modOps = operations.Where(op => op.Type == OperationType.RecordModInstallation).ToList();
                
                _logger.Debug("Batch breakdown: {StatOps} stat operations, {ModOps} mod installation operations", 
                    statOps.Count, modOps.Count);
                
                // Process stat operations in batch
                if (statOps.Count > 0)
                {
                    _logger.Debug("Processing {Count} stat operations", statOps.Count);
                    ProcessStatOperations(statsCollection, statOps);
                }
                
                // Process mod installation operations in batch
                if (modOps.Count > 0)
                {
                    _logger.Debug("Processing {Count} mod installation operations", modOps.Count);
                    ProcessModOperations(modsCollection, modOps);
                }
                
                _logger.Info("Successfully processed batch of {Count} operations", operations.Count);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to process batch operations - will retry failed operations");
                
                // Re-queue failed operations with exponential backoff
                foreach (var op in operations)
                {
                    op.RetryCount++;
                    if (op.RetryCount <= 3)
                    {
                        var delaySeconds = Math.Pow(2, op.RetryCount);
                        _logger.Warn("Re-queueing operation {Type} for {Item} (retry {Retry}/3) after {Delay}s delay", 
                            op.Type, op.ModName ?? op.StatName, op.RetryCount, delaySeconds);
                        
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _cancellationTokenSource.Token);
                        await _operationWriter.WriteAsync(op, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        _logger.Error("Operation {Type} for {Item} failed after 3 retries - dropping operation", 
                            op.Type, op.ModName ?? op.StatName);
                    }
                }
                
                throw;
            }
        }, "Failed to process batch operations", false);
    }

    private void ProcessStatOperations(ILiteCollection<StatRecord> collection, List<DatabaseOperation> operations)
    {
        if (operations.Count == 0) return;
        
        _logger.Debug("Processing {Count} stat operations", operations.Count);
        
        // Group by stat name for batching
        var statGroups = operations.GroupBy(op => op.StatName);
        
        foreach (var group in statGroups)
        {
            var statName = group.Key;
            var incrementCount = group.Count();
            
            _logger.Debug("Processing stat '{StatName}' with {Count} increments", statName, incrementCount);
            
            var existingRecord = collection.FindOne(x => x.Name == statName);
            
            if (existingRecord == null)
            {
                var newRecord = new StatRecord
                {
                    Name = statName,
                    Count = incrementCount
                };
                collection.Insert(newRecord);
                
                // Update cache AFTER successful database write
                _statCache[statName] = (incrementCount, DateTime.UtcNow);
                
                _logger.Info("Created new stat record '{StatName}' with initial count {Count}", statName, incrementCount);
            }
            else
            {
                var oldCount = existingRecord.Count;
                existingRecord.Count += incrementCount;
                collection.Update(existingRecord);
                
                // Update cache AFTER successful database write
                _statCache[statName] = (existingRecord.Count, DateTime.UtcNow);
                
                _logger.Info("Updated stat '{StatName}' from {OldCount} to {NewCount} (+{Increment})", 
                    statName, oldCount, existingRecord.Count, incrementCount);
            }
        }
        
        _logger.Debug("Completed processing {Count} stat operations", operations.Count);
    }

    private void ProcessModOperations(ILiteCollection<ModInstallationRecord> collection, List<DatabaseOperation> operations)
    {
        if (operations.Count == 0) return;
        
        _logger.Debug("Processing {Count} mod installation operations", operations.Count);
        
        var records = operations.Select(op => new ModInstallationRecord
        {
            ModName = op.ModName,
            InstallationTime = op.Timestamp
        }).ToList();
        
        _logger.Debug("Inserting {Count} mod installation records: {ModNames}", 
            records.Count, string.Join(", ", records.Select(r => r.ModName)));
        
        collection.InsertBulk(records);
        
        _logger.Info("Successfully inserted {Count} mod installation records", records.Count);
    }

    public async Task IncrementStatAsync(Stat stat)
    {
        _logger.Debug("IncrementStatAsync called for stat: {Stat}", stat);
        
        var operation = new DatabaseOperation
        {
            Type = OperationType.IncrementStat,
            StatName = stat.ToString(),
            Timestamp = DateTime.UtcNow
        };

        _logger.Debug("Attempting to queue increment operation for stat: {Stat}", stat);

        if (!_operationWriter.TryWrite(operation))
        {
            _logger.Debug("TryWrite failed for stat {Stat}, using WriteAsync", stat);
            await _operationWriter.WriteAsync(operation, _cancellationTokenSource.Token);
            _logger.Debug("WriteAsync completed for stat {Stat}", stat);
        }
        else
        {
            _logger.Debug("Successfully queued increment operation for stat {Stat} via TryWrite", stat);
        }
            
        _logger.Debug("IncrementStatAsync completed for stat: {Stat}", stat);
    }

    public async Task<int> GetStatCountAsync(Stat stat)
    {
        var statName = stat.ToString();
        
        // Check cache first
        if (_statCache.TryGetValue(statName, out var cached) && 
            DateTime.UtcNow - cached.lastUpdated < _cacheExpiry)
        {
            _logger.Debug("Retrieved stat {Stat} from cache: {Value}", stat, cached.value);
            return cached.value;
        }

        _logger.Debug("Cache miss for stat {Stat}, querying database", stat);

        // Wait for database initialization if needed
        try
        {
            await _databaseInitialized.Task;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Database not initialized, returning default value for stat {Stat}", stat);
            return 0;
        }

        // Fallback to database
        return await ExecuteDatabaseActionAsync(db =>
        {
            var stats = db.GetCollection<StatRecord>("stats");
            var statRecord = stats.FindOne(x => x.Name == statName);
            
            var count = statRecord?.Count ?? 0;
            
            // Update cache AFTER successful database read
            _statCache[statName] = (count, DateTime.UtcNow);
            
            _logger.Debug("Retrieved stat {Stat} from database: {Value}", stat, count);
            
            return Task.FromResult(count);
        }, $"Failed to retrieve statistic '{stat}'", 0);
    }

    public async Task<int> GetModsInstalledTodayAsync()
    {
        // Wait for database initialization if needed
        try
        {
            await _databaseInitialized.Task;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Database not initialized, returning 0 for mods installed today");
            return 0;
        }

        return await ExecuteDatabaseActionAsync(db =>
        {
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            var startOfToday = DateTime.UtcNow.Date;
            
            var count = modInstallations.Count(x => x.InstallationTime >= startOfToday);
            _logger.Debug("Retrieved mods installed today: {Count}", count);
            
            return Task.FromResult(count);
        }, "Failed to retrieve mods installed today", 0);
    }

    public async Task RecordModInstallationAsync(string modName)
    {
        _logger.Info("RecordModInstallationAsync called for mod: {ModName}", modName);
        
        var operation = new DatabaseOperation
        {
            Type = OperationType.RecordModInstallation,
            ModName = modName,
            Timestamp = DateTime.UtcNow
        };

        _logger.Debug("Attempting to queue mod installation operation for: {ModName}", modName);

        if (!_operationWriter.TryWrite(operation))
        {
            _logger.Debug("TryWrite failed for mod {ModName}, using WriteAsync", modName);
            await _operationWriter.WriteAsync(operation, _cancellationTokenSource.Token);
            _logger.Debug("WriteAsync completed for mod {ModName}", modName);
        }
        else
        {
            _logger.Debug("Successfully queued mod installation operation for {ModName} via TryWrite", modName);
        }

        _logger.Debug("About to increment ModsInstalled stat for mod: {ModName}", modName);
        await IncrementStatAsync(Stat.ModsInstalled);
        _logger.Info("RecordModInstallationAsync completed for mod: {ModName}", modName);
    }

    public async Task<ModInstallationRecord?> GetMostRecentModInstallationAsync()
    {
        // Wait for database initialization if needed
        try
        {
            await _databaseInitialized.Task;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Database not initialized, returning null for most recent mod installation");
            return null;
        }

        return await ExecuteDatabaseActionAsync(db =>
        {
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            var result = modInstallations
                .FindAll()
                .OrderByDescending(x => x.InstallationTime)
                .FirstOrDefault();
            
            _logger.Debug("Retrieved most recent mod installation: {ModName} at {Time}", 
                result?.ModName ?? "None", result?.InstallationTime);
            
            return Task.FromResult(result);
        }, "Failed to retrieve the most recent mod installation");
    }

    public async Task<int> GetUniqueModsInstalledCountAsync()
    {
        // Wait for database initialization if needed
        try
        {
            await _databaseInitialized.Task;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Database not initialized, returning 0 for unique mods count");
            return 0;
        }

        return await ExecuteDatabaseActionAsync(db =>
        {
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            var uniqueCount = modInstallations
                .FindAll()
                .Select(x => x.ModName)
                .Distinct()
                .Count();
            
            _logger.Debug("Retrieved unique mods installed count: {Count}", uniqueCount);
            
            return Task.FromResult(uniqueCount);
        }, "Failed to retrieve count of unique mods installed", 0);
    }

    public async Task<List<ModInstallationRecord>> GetAllInstalledModsAsync()
    {
        // Wait for database initialization if needed
        try
        {
            await _databaseInitialized.Task;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Database not initialized, returning empty list for all installed mods");
            return new List<ModInstallationRecord>();
        }

        return await ExecuteDatabaseActionAsync(db =>
        {
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            var mods = modInstallations
                .FindAll()
                .OrderByDescending(x => x.InstallationTime)
                .ToList();
            
            _logger.Debug("Retrieved all installed mods: {Count} total", mods.Count);
            
            return Task.FromResult(mods);
        }, "Failed to retrieve all installed mods", new List<ModInstallationRecord>());
    }

    private async Task<TResult> ExecuteDatabaseActionAsync<TResult>(
        Func<LiteDatabase, Task<TResult>> action,
        string errorContext,
        TResult defaultValue = default)
    {
        _logger.Debug("Executing database action: {Context}", errorContext);
        
        const int maxRetries = 3;
        const int baseDelayMs = 50;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Wait for in-process semaphore only
                await _globalDbSemaphore.WaitAsync(_cancellationTokenSource.Token);
                
                try
                {
                    var connectionString = $"Filename={_databasePath};Connection=Shared;Timeout=00:00:30";
                    
                    using var database = new LiteDatabase(connectionString);
                    
                    var result = await action(database);
                    _logger.Debug("Database action completed successfully: {Context}", errorContext);
                    return result;
                }
                finally
                {
                    _globalDbSemaphore.Release();
                }
            }
            catch (Exception ex) when (IsRetryableException(ex) && attempt < maxRetries)
            {
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt - 1); // Exponential backoff
                _logger.Warn(ex, "Database action failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms: {Context}", 
                    attempt, maxRetries, delayMs, errorContext);
                
                await Task.Delay(delayMs, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Database action failed permanently after {Attempt} attempts: {Context}", attempt, errorContext);
                return defaultValue!;
            }
        }
        
        _logger.Error("Database action failed after {MaxRetries} attempts: {Context}", maxRetries, errorContext);
        return defaultValue!;
    }

    private static bool IsRetryableException(Exception ex)
    {
        return ex is IOException ||
               ex is UnauthorizedAccessException ||
               ex is LiteException liteEx && (
                   liteEx.Message.Contains("database is locked") ||
                   liteEx.Message.Contains("lock timeout") ||
                   liteEx.Message.Contains("Database timeout") ||
                   liteEx.Message.Contains("timeout")
               );
    }

    private async Task InitializeDatabaseAsync()
    {
        _logger.Info("Initializing database at: {DatabasePath}", _databasePath);
        
        var directoryPath = Path.GetDirectoryName(_databasePath);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            _logger.Info("Created missing directory for database at '{DirectoryPath}'", directoryPath);
        }

        // Initialise with proper connection settings
        await ExecuteDatabaseActionAsync<bool>(db =>
        {
            // Ensure collections exist and create indexes
            var stats = db.GetCollection<StatRecord>("stats");
            stats.EnsureIndex(x => x.Name, true); // Unique index on Name
        
            var modInstallations = db.GetCollection<ModInstallationRecord>("mod_installations");
            modInstallations.EnsureIndex(x => x.ModName);
            modInstallations.EnsureIndex(x => x.InstallationTime);
        
            _logger.Info("Database initialized successfully with collections and indexes");
            
            return Task.FromResult(true);
        }, "Initialize database", false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _logger.Info("StatisticService disposal starting");
        _disposed = true;
        
        // Signal shutdown and wait for background processor to complete
        _operationWriter.Complete();
        _cancellationTokenSource.Cancel();
        
        try
        {
            _backgroundProcessor.Wait(TimeSpan.FromSeconds(5));
            _logger.Info("Background processor completed gracefully");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Background processor did not complete gracefully");
        }
        
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