using System.Collections.Concurrent;
using CommonLib.Consts;
using CommonLib.Extensions;
using CommonLib.Interfaces;
using Newtonsoft.Json;
using NLog;
using PenumbraModForwarder.FileMonitor.Interfaces;
using PenumbraModForwarder.FileMonitor.Models;

namespace PenumbraModForwarder.FileMonitor.Services;

public sealed class FileQueueProcessor : IFileQueueProcessor
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<string, DateTime> _fileQueue;
    private readonly ConcurrentDictionary<string, int> _retryCounts;
    private readonly IFileStorage _fileStorage;
    private readonly IConfigurationService _configurationService;
    private readonly IFileProcessor _fileProcessor;

    private CancellationTokenSource _cancellationTokenSource;
    private Task _processingTask;
    private Timer _persistenceTimer;
    private readonly string _stateFilePath;

    public event EventHandler<FileMovedEvent> FileMoved;
    public event EventHandler<FilesExtractedEventArgs> FilesExtracted;

    public FileQueueProcessor(
        IFileStorage fileStorage,
        IConfigurationService configurationService,
        IFileProcessor fileProcessor)
    {
        _fileQueue = new ConcurrentDictionary<string, DateTime>();
        _retryCounts = new ConcurrentDictionary<string, int>();

        _fileStorage = fileStorage;
        _configurationService = configurationService;
        _fileProcessor = fileProcessor;

        _stateFilePath = Path.Combine(ConfigurationConsts.FileWatcherState, "fileQueueState.json");
    }

    /// <summary>
    /// Adds a file to the queue if it's not in the ignore list.
    /// </summary>
    public void EnqueueFile(string fullPath)
    {
        if (IgnoreList.IgnoreListStrings.Contains(fullPath, StringComparer.InvariantCultureIgnoreCase))
        {
            _logger.Info("Ignoring file (on ignore list): {FullPath}", fullPath);
            return;
        }

        _fileQueue[fullPath] = DateTime.UtcNow;
        _retryCounts[fullPath] = 0;
        _logger.Debug("Enqueued file: {FullPath}", fullPath);
    }

    /// <summary>
    /// Handles a file rename, preserving queue status if the old path was tracked.
    /// </summary>
    public void RenameFileInQueue(string oldPath, string newPath)
    {
        if (IgnoreList.IgnoreListStrings.Contains(newPath, StringComparer.InvariantCultureIgnoreCase))
        {
            _logger.Info("Ignoring renamed file (on ignore list): {FullPath}", newPath);
            _fileQueue.TryRemove(oldPath, out _);
            _retryCounts.TryRemove(oldPath, out _);
            return;
        }

        if (_fileQueue.TryRemove(oldPath, out var timeAdded))
        {
            _fileQueue[newPath] = timeAdded;
            if (_retryCounts.TryRemove(oldPath, out var oldCount))
            {
                _retryCounts[newPath] = oldCount;
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

    /// <summary>
    /// Loads the persisted queue state from disk, skipping or removing invalid/ignored files.
    /// </summary>
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
                        // Check if it's on the ignore list
                        if (IgnoreList.IgnoreListStrings.Contains(kvp.Key, StringComparer.InvariantCultureIgnoreCase))
                        {
                            _logger.Info("Skipping ignored file from state: {FullPath}", kvp.Key);
                            continue;
                        }

                        // This is a very rare bug that will cause the program to fail all the time
                        if (!_fileStorage.Exists(kvp.Key))
                        {
                            _logger.Warn("File from state no longer exists. Removing from queue: {FullPath}", kvp.Key);
                            _fileQueue.TryRemove(kvp.Key, out _);
                            _retryCounts.TryRemove(kvp.Key, out _);
                            continue;
                        }

                        // Otherwise, file is valid, add it to queue
                        _fileQueue[kvp.Key] = kvp.Value;
                        _retryCounts[kvp.Key] = 0;
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

    /// <summary>
    /// Persists the current queue state to disk.
    /// </summary>
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

    /// <summary>
    /// Starts the processing task and initialises the persistence timer.
    /// </summary>
    public void StartProcessing()
    {
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

    /// <summary>
    /// Continuously processes the file queue.
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var filesToProcess = _fileQueue.Keys.ToList();
                var hasChanges = false;

                foreach (var filePath in filesToProcess.TakeWhile(filePath => !cancellationToken.IsCancellationRequested))
                {
                    // Remove ignored files from the queue
                    if (IgnoreList.IgnoreListStrings.Contains(filePath, StringComparer.InvariantCultureIgnoreCase))
                    {
                        if (_fileQueue.TryRemove(filePath, out _) | _retryCounts.TryRemove(filePath, out _))
                        {
                            _logger.Info("Removed ignored file from queue: {FullPath}", filePath);
                            hasChanges = true;
                        }
                        continue;
                    }

                    // If the file no longer exists, remove it
                    if (!_fileStorage.Exists(filePath))
                    {
                        if (_fileQueue.TryRemove(filePath, out _) | _retryCounts.TryRemove(filePath, out _))
                        {
                            _logger.Warn("File not found, removing from queue: {FullPath}", filePath);
                            hasChanges = true;
                        }
                        continue;
                    }

                    // Log file readiness check
                    var isReady = _fileProcessor.IsFileReady(filePath);
                    _logger.Debug("File readiness check for {FullPath}: {IsReady}", filePath, isReady);

                    // If a file is ready, process it
                    if (isReady)
                    {
                        _retryCounts[filePath] = 0;
                        await _fileProcessor.ProcessFileAsync(
                            filePath,
                            cancellationToken,
                            OnFileMoved,
                            OnFilesExtracted
                        );

                        if (!(_fileQueue.TryRemove(filePath, out _) | _retryCounts.TryRemove(filePath, out _)))
                            continue;
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
        FileMoved.Invoke(this, e);
    }

    private void OnFilesExtracted(object sender, FilesExtractedEventArgs e)
    {
        _logger.Debug("Files extracted event fired for: {FullPath}", e);
        FilesExtracted.Invoke(this, e);
    }
}