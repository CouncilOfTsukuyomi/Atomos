using System.Collections.Concurrent;
using CommonLib.Consts;
using CommonLib.Extensions;
using CommonLib.Interfaces;
using Newtonsoft.Json;
using NLog;
using PenumbraModForwarder.FileMonitor.Interfaces;
using PenumbraModForwarder.FileMonitor.Models;

namespace PenumbraModForwarder.FileMonitor.Services;

public sealed class FileQueueProcessor : IFileQueueProcessor, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<string, DateTime> _fileQueue;
    private readonly ConcurrentDictionary<string, int> _retryCounts;
    private readonly ConcurrentDictionary<string, string> _fileTaskIds;
    private readonly IFileStorage _fileStorage;
    private readonly IConfigurationService _configurationService;
    private readonly IFileProcessor _fileProcessor;

    private CancellationTokenSource _cancellationTokenSource;
    private Task _processingTask;
    private Timer _persistenceTimer;
    private readonly string _stateFilePath;

    private bool _disposed = false;

    public event EventHandler<FileMovedEvent> FileMoved;
    public event EventHandler<FilesExtractedEventArgs> FilesExtracted;

    public FileQueueProcessor(
        IFileStorage fileStorage,
        IConfigurationService configurationService,
        IFileProcessor fileProcessor)
    {
        _fileQueue = new ConcurrentDictionary<string, DateTime>();
        _retryCounts = new ConcurrentDictionary<string, int>();
        _fileTaskIds = new ConcurrentDictionary<string, string>();

        _fileStorage = fileStorage;
        _configurationService = configurationService;
        _fileProcessor = fileProcessor;

        _stateFilePath = Path.Combine(ConfigurationConsts.FileWatcherState, "fileQueueState.json");
        
        _fileProcessor.FileMoved += OnFileMoved;
        _fileProcessor.FilesExtracted += OnFilesExtracted;
    }

    public void EnqueueFile(string fullPath)
    {
        fullPath = Path.GetFullPath(fullPath);

        if (IgnoreList.IgnoreListStrings.Contains(fullPath, StringComparer.InvariantCultureIgnoreCase))
        {
            _logger.Info("Ignoring file (on ignore list): {FullPath}", fullPath);
            return;
        }

        if (_fileTaskIds.ContainsKey(fullPath))
        {
            _logger.Warn("File {FullPath} is being re-enqueued! Old taskId: {OldTaskId}, will KEEP the old one.",
                fullPath, _fileTaskIds[fullPath]);
            _retryCounts[fullPath] = 0;
            return;
        }

        _fileQueue[fullPath] = DateTime.UtcNow;
        _retryCounts[fullPath] = 0;
        _fileTaskIds[fullPath] = Guid.NewGuid().ToString();
        _logger.Debug("Enqueued file: {FullPath}", fullPath);
    }

    public void RenameFileInQueue(string oldPath, string newPath)
    {
        if (IgnoreList.IgnoreListStrings.Contains(newPath, StringComparer.InvariantCultureIgnoreCase))
        {
            _logger.Info("Ignoring renamed file (on ignore list): {FullPath}", newPath);
            _fileQueue.TryRemove(oldPath, out _);
            _retryCounts.TryRemove(oldPath, out _);
            _fileTaskIds.TryRemove(oldPath, out _);
            return;
        }

        if (_fileQueue.TryRemove(oldPath, out var timeAdded))
        {
            _fileQueue[newPath] = timeAdded;
            if (_retryCounts.TryRemove(oldPath, out var oldCount))
            {
                _retryCounts[newPath] = oldCount;
            }
            if (_fileTaskIds.TryRemove(oldPath, out var oldTaskId))
            {
                _fileTaskIds[newPath] = oldTaskId;
            }
            _logger.Debug("Renamed file in queue from {OldPath} to {NewPath}", oldPath, newPath);
        }
        else
        {
            var extension = Path.GetExtension(newPath)?.ToLowerInvariant();
            if (!FileExtensionsConsts.AllowedExtensions.Contains(extension)) return;
            EnqueueFile(newPath);
            _logger.Info("File added to queue after rename (unrecognized old path): {FullPath}", newPath);
        }
    }

    public async Task LoadStateAsync()
    {
        try
        {
            if (_fileStorage.Exists(_stateFilePath))
            {
                var serializedQueue = _fileStorage.Read(_stateFilePath);
                var deserializedQueue = JsonConvert.DeserializeObject<ConcurrentDictionary<string, DateTime>>(serializedQueue);
                if (deserializedQueue != null)
                {
                    foreach (var kvp in deserializedQueue)
                    {
                        if (IgnoreList.IgnoreListStrings.Contains(kvp.Key, StringComparer.InvariantCultureIgnoreCase))
                        {
                            _logger.Info("Skipping ignored file from state: {FullPath}", kvp.Key);
                            continue;
                        }

                        if (!_fileStorage.Exists(kvp.Key))
                        {
                            _logger.Warn("File from state no longer exists. Removing from queue: {FullPath}", kvp.Key);
                            _fileQueue.TryRemove(kvp.Key, out _);
                            _retryCounts.TryRemove(kvp.Key, out _);
                            _fileTaskIds.TryRemove(kvp.Key, out _);
                            continue;
                        }

                        _fileQueue[kvp.Key] = kvp.Value;
                        _retryCounts[kvp.Key] = 0;
                        if (!_fileTaskIds.ContainsKey(kvp.Key))
                        {
                            _fileTaskIds[kvp.Key] = Guid.NewGuid().ToString();
                        }
                    }
                }
                _logger.Info("File queue state loaded successfully.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load file queue state.");
        }

        await Task.CompletedTask;
    }

    public void PersistState()
    {
        try
        {
            var serializedQueue = JsonConvert.SerializeObject(_fileQueue);
            _fileStorage.Write(_stateFilePath, serializedQueue);
            _logger.Debug("File queue state persisted.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist file queue state.");
        }
    }

    public void StartProcessing()
    {
        if (_processingTask != null && !_processingTask.IsCompleted)
        {
            _logger.Warn("Processing task is already running.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _processingTask = ProcessQueueAsync(_cancellationTokenSource.Token);

        _persistenceTimer = new Timer(
            _ => PersistState(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1)
        );
        _logger.Info("Started processing task and persistence timer.");
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var filesToProcess = _fileQueue.Keys.ToList();
                var hasChanges = false;

                foreach (var filePath in filesToProcess.TakeWhile(_ => !cancellationToken.IsCancellationRequested))
                {
                    if (IgnoreList.IgnoreListStrings.Contains(filePath, StringComparer.InvariantCultureIgnoreCase))
                    {
                        if (_fileQueue.TryRemove(filePath, out _) |
                            _retryCounts.TryRemove(filePath, out _) |
                            _fileTaskIds.TryRemove(filePath, out _))
                        {
                            _logger.Info("Removed ignored file from queue: {FullPath}", filePath);
                            hasChanges = true;
                        }
                        continue;
                    }

                    if (!_fileStorage.Exists(filePath))
                    {
                        if (_fileQueue.TryRemove(filePath, out _) |
                            _retryCounts.TryRemove(filePath, out _) |
                            _fileTaskIds.TryRemove(filePath, out _))
                        {
                            _logger.Warn("File not found, removing from queue: {FullPath}", filePath);
                            hasChanges = true;
                        }
                        continue;
                    }

                    var isReady = _fileProcessor.IsFileReady(filePath);
                    _logger.Debug("File readiness check for {FullPath}: {IsReady}", filePath, isReady);

                    if (isReady)
                    {
                        _retryCounts[filePath] = 0;

                        if (!_fileTaskIds.TryGetValue(filePath, out var taskId))
                        {
                            taskId = Guid.NewGuid().ToString();
                            _fileTaskIds[filePath] = taskId;
                        }

                        await _fileProcessor.ProcessFileAsync(
                            filePath,
                            cancellationToken,
                            taskId
                        );

                        _fileQueue.TryRemove(filePath, out _);
                        _retryCounts.TryRemove(filePath, out _);
                        _fileTaskIds.TryRemove(filePath, out _);

                        _logger.Info("Processed and removed file from queue: {FullPath}", filePath);
                        hasChanges = true;
                    }
                    else
                    {
                        var currentRetry = _retryCounts.AddOrUpdate(filePath, 1, (_, oldValue) => oldValue + 1);
                        _logger.Debug("File not ready (attempt {Attempt}) for file: {FullPath}", currentRetry, filePath);

                        if (currentRetry < 5)
                        {
                            if (currentRetry <= 3)
                            {
                                _logger.Info("File not ready (attempt {Attempt}), requeue: {FullPath}", currentRetry, filePath);
                            }
                            else
                            {
                                _logger.Debug("File not ready (attempt {Attempt}), requeue: {FullPath}", currentRetry, filePath);
                            }
                        }
                        else
                        {
                            if (currentRetry % 5 == 0)
                            {
                                _logger.Warn("File not ready after {Attempt} attempts: {FullPath}", currentRetry, filePath);
                            }
                            else
                            {
                                _logger.Debug("File not ready after {Attempt} attempts: {FullPath}", currentRetry, filePath);
                            }
                        }
                    }
                }

                if (hasChanges)
                {
                    PersistState();
                }

                await Task.Delay(500, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.Info("Processing queue was canceled.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing queue.");
        }
    }

    private void OnFileMoved(object sender, FileMovedEvent e)
    {
        _logger.Debug("File moved event fired for: {FullPath}", e);
        FileMoved?.Invoke(this, e);
    }

    private void OnFilesExtracted(object sender, FilesExtractedEventArgs e)
    {
        _logger.Debug("Files extracted event fired for: {FullPath}", e);
        FilesExtracted?.Invoke(this, e);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        if (disposing)
        {
            _logger.Info("Disposing FileQueueProcessor...");
            _cancellationTokenSource?.Cancel();
            try
            {
                _processingTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch { }
            _processingTask = null;
            _persistenceTimer?.Dispose();
        }
        _disposed = true;
    }
}