using System.Collections.Concurrent;
using Newtonsoft.Json;
using NLog;
using PenumbraModForwarder.Common.Consts;
using PenumbraModForwarder.Common.Interfaces;
using PenumbraModForwarder.FileMonitor.Interfaces;
using PenumbraModForwarder.FileMonitor.Models;
using PenumbraModForwarder.Common.Extensions;

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

        _stateFilePath = Path.Combine(ConfigurationConsts.ConfigurationPath, "fileQueueState.json");
    }

    /// <summary>
    /// Adds a file to the queue if it's not in the ignore list.
    /// </summary>
    /// <param name="fullPath">The full path to enqueue.</param>
    public void EnqueueFile(string fullPath)
    {
        // Skip enqueuing if this file is in the ignore list
        if (IgnoreList.IgnoreListStrings.Contains(fullPath, StringComparer.InvariantCultureIgnoreCase))
        {
            _logger.Info("Ignoring file (on ignore list): {FullPath}", fullPath);
            return;
        }

        _fileQueue[fullPath] = DateTime.UtcNow;
        _retryCounts[fullPath] = 0;
    }

    /// <summary>
    /// Handles a file rename, preserving queue status if the old path was tracked.
    /// </summary>
    /// <param name="oldPath">The old file path.</param>
    /// <param name="newPath">The new file path.</param>
    public void RenameFileInQueue(string oldPath, string newPath)
    {
        // If the new path is in the ignore list, don't rename or track it
        if (IgnoreList.IgnoreListStrings.Contains(newPath, StringComparer.InvariantCultureIgnoreCase))
        {
            _logger.Info("Ignoring renamed file (on ignore list): {FullPath}", newPath);

            // If old path was in the queue, remove it
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
        }
        else
        {
            var extension = Path.GetExtension(newPath)?.ToLowerInvariant();
            if (FileExtensionsConsts.AllowedExtensions.Contains(extension))
            {
                EnqueueFile(newPath);
                _logger.Info("File added to queue after rename (unrecognized old path): {FullPath}", newPath);
            }
        }
    }

    /// <summary>
    /// Loads the persisted queue state from disk, skipping ignored files.
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
                        // Skip if the file is in the ignore list
                        if (IgnoreList.IgnoreListStrings.Contains(kvp.Key, StringComparer.InvariantCultureIgnoreCase))
                        {
                            _logger.Info("Skipping ignored file from state: {FullPath}", kvp.Key);
                            continue;
                        }

                        if (_fileStorage.Exists(kvp.Key))
                        {
                            _fileQueue[kvp.Key] = kvp.Value;
                            _retryCounts[kvp.Key] = 0;
                        }
                        else
                        {
                            _logger.Warn("File from state no longer exists: {FullPath}", kvp.Key);
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

    /// <summary>
    /// Persists the current queue state to disk.
    /// </summary>
    public void PersistState()
    {
        try
        {
            var serializedQueue = JsonConvert.SerializeObject(_fileQueue);
            _fileStorage.Write(_stateFilePath, serializedQueue);
            _logger.Info("File queue state persisted.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist file queue state.");
        }
    }

    /// <summary>
    /// Starts the processing task and initializes the persistence timer.
    /// </summary>
    public void StartProcessing()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _processingTask = ProcessQueueAsync(_cancellationTokenSource.Token);

        // Save state every minute
        _persistenceTimer = new Timer(
            _ => PersistState(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1)
        );
    }

    /// <summary>
    /// Continuously processes the file queue, skipping any files on the ignore list.
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var filesToProcess = _fileQueue.Keys.ToList();
                var hasChanges = false;

                foreach (var filePath in filesToProcess)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // If the file is in the ignore list, remove it from the queue
                    if (IgnoreList.IgnoreListStrings.Contains(filePath, StringComparer.InvariantCultureIgnoreCase))
                    {
                        if (_fileQueue.TryRemove(filePath, out _) | _retryCounts.TryRemove(filePath, out _))
                        {
                            _logger.Info("Removed ignored file from queue: {FullPath}", filePath);
                            hasChanges = true;
                        }
                        continue;
                    }

                    // If it doesn't exist anymore, remove it
                    if (!_fileStorage.Exists(filePath))
                    {
                        if (_fileQueue.TryRemove(filePath, out _) | _retryCounts.TryRemove(filePath, out _))
                        {
                            _logger.Warn("File not found, removing from queue: {FullPath}", filePath);
                            hasChanges = true;
                        }
                        continue;
                    }

                    // If file is ready, try processing it
                    if (_fileProcessor.IsFileReady(filePath))
                    {
                        _retryCounts[filePath] = 0;

                        await _fileProcessor.ProcessFileAsync(
                            filePath,
                            cancellationToken,
                            OnFileMoved,
                            OnFilesExtracted
                        );

                        if (_fileQueue.TryRemove(filePath, out _) | _retryCounts.TryRemove(filePath, out _))
                        {
                            hasChanges = true;
                        }
                    }
                    else
                    {
                        var currentRetry = _retryCounts.AddOrUpdate(filePath, 1, (_, oldValue) => oldValue + 1);

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

    private void OnFileMoved(object sender, FileMovedEvent e) => FileMoved?.Invoke(this, e);
    private void OnFilesExtracted(object sender, FilesExtractedEventArgs e) => FilesExtracted?.Invoke(this, e);
}