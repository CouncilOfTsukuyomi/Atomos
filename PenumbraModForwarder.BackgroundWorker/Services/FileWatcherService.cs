using System.Collections.Concurrent;
using CommonLib.Events;
using CommonLib.Interfaces;
using CommonLib.Models;
using Newtonsoft.Json;
using NLog;
using PenumbraModForwarder.BackgroundWorker.Events;
using PenumbraModForwarder.BackgroundWorker.Interfaces;
using PenumbraModForwarder.FileMonitor.Events;
using PenumbraModForwarder.FileMonitor.Interfaces;
using PenumbraModForwarder.FileMonitor.Models;

namespace PenumbraModForwarder.BackgroundWorker.Services;

public class FileWatcherService : IFileWatcherService, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly IWebSocketServer _webSocketServer;
    private readonly IServiceProvider _serviceProvider;
    private readonly IModHandlerService _modHandlerService;

    private IFileWatcher? _fileWatcher;
    private bool _eventsSubscribed;

    // Dictionary to track pending user selections
    private readonly ConcurrentDictionary<string, TaskCompletionSource<List<string>>> _pendingSelections = new();

    public FileWatcherService(
        IConfigurationService configurationService,
        IWebSocketServer webSocketServer,
        IServiceProvider serviceProvider,
        IModHandlerService modHandlerService)
    {
        _configurationService = configurationService;
        _webSocketServer = webSocketServer;
        _serviceProvider = serviceProvider;
        _modHandlerService = modHandlerService;

        _configurationService.ConfigurationChanged += OnConfigurationChanged;
        _webSocketServer.MessageReceived += OnWebSocketMessageReceived;
    }

    public async Task Start()
    {
        await InitializeFileWatcherAsync();
    }

    public void Stop()
    {
        DisposeFileWatcher();
    }

    private async Task InitializeFileWatcherAsync()
    {
        _logger.Debug("Initializing FileWatcher...");

        var downloadPaths = _configurationService
            .ReturnConfigValue(config => config.BackgroundWorker.DownloadPath) as List<string>;

        // Ensure distinct, valid paths
        downloadPaths = downloadPaths?.Where(Directory.Exists).Distinct().ToList();

        if (downloadPaths == null || downloadPaths.Count == 0)
        {
            _logger.Warn("No valid download paths specified. FileWatcher will not be initialized.");
            return;
        }

        try
        {
            _logger.Debug("Resolving new IFileWatcher instance...");
            _fileWatcher = _serviceProvider.GetRequiredService<IFileWatcher>();

            if (!_eventsSubscribed)
            {
                _fileWatcher.FileMoved += OnFileMoved;
                _fileWatcher.FilesExtracted += OnFilesExtracted;
                _fileWatcher.ExtractionProgressChanged += OnExtractionProgressChanged;
                _eventsSubscribed = true;
                _logger.Debug("Event handlers attached.");
            }

            _logger.Debug("Starting watchers for the following paths:");
            foreach (var downloadPath in downloadPaths)
            {
                _logger.Debug(" - {DownloadPath}", downloadPath);
            }

            await _fileWatcher.StartWatchingAsync(downloadPaths!);
            _logger.Debug("FileWatcher started successfully.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while initializing the file watcher.");
        }
    }

    private async Task RestartFileWatcherAsync()
    {
        _logger.Debug("Restarting FileWatcher...");
        DisposeFileWatcher();
        await InitializeFileWatcherAsync();
        _logger.Debug("FileWatcher restarted successfully.");
    }

    private void DisposeFileWatcher()
    {
        if (_fileWatcher != null)
        {
            if (_eventsSubscribed)
            {
                _fileWatcher.FileMoved -= OnFileMoved;
                _fileWatcher.FilesExtracted -= OnFilesExtracted;
                _fileWatcher.ExtractionProgressChanged -= OnExtractionProgressChanged;
                _eventsSubscribed = false;
            }

            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }

    private async void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        try
        {
            _logger.Debug("Configuration: {PropertyName} changed to: {NewValue}", e.PropertyName, e.NewValue);

            // If the download path is changed, restart the file watcher
            if (e.PropertyName == "BackgroundWorker.DownloadPath")
            {
                _logger.Info("Configuration changed. Restarting FileWatcher");
                await RestartFileWatcherAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred in OnConfigurationChanged.");
        }
    }
    
    private async void OnExtractionProgressChanged(object? sender, ExtractionProgressChangedEventArgs e)
    {
        _logger.Info($"Extraction progress: {e.Progress}");
        var progressMessage = WebSocketMessage.CreateProgress(
            e.TaskId,
            e.Progress,
            e.Message
        );

        try
        {
            await _webSocketServer.BroadcastToEndpointAsync("/status", progressMessage);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to broadcast extraction progress");
        }
    }


    private void OnFileMoved(object? sender, FileMovedEvent e)
    {
        _logger.Info("File moved: {DestinationPath}", e.DestinationPath);
        _modHandlerService.HandleFileAsync(e.DestinationPath).GetAwaiter().GetResult();
    }

    private void OnFilesExtracted(object? sender, FilesExtractedEventArgs e)
    {
        _logger.Info("Files extracted from archive: {ArchiveFileName}", e.ArchiveFileName);

        var taskId = Guid.NewGuid().ToString();
        var installAll = (bool)_configurationService.ReturnConfigValue(config => config.BackgroundWorker.InstallAll);
        var deleteUnselected = (bool)_configurationService.ReturnConfigValue(config => config.BackgroundWorker.AutoDelete);

        if (!installAll)
        {
            // Not installing all automatically; prompt user for selection

            var extractedFilesJson = JsonConvert.SerializeObject(e.ExtractedFilePaths);

            var message = new WebSocketMessage
            {
                Type = WebSocketMessageType.Status,
                TaskId = taskId,
                Status = "select_files",
                Progress = 0,
                Message = extractedFilesJson
            };

            // Send prompt to user
            _webSocketServer.BroadcastToEndpointAsync("/install", message).GetAwaiter().GetResult();

            // Wait for user selection
            var selectedFiles = WaitForUserSelection(taskId);

            if (selectedFiles != null && selectedFiles.Any())
            {
                foreach (var selectedFile in selectedFiles)
                {
                    _modHandlerService.HandleFileAsync(selectedFile).GetAwaiter().GetResult();
                }

                if (deleteUnselected)
                {
                    var unselectedFiles = e.ExtractedFilePaths.Except(selectedFiles).ToList();
                    foreach (var unselectedFile in unselectedFiles)
                    {
                        TryDeleteFile(unselectedFile);
                    }
                }

                var completionMessage = WebSocketMessage.CreateStatus(
                    taskId,
                    WebSocketMessageStatus.Completed,
                    "Selected files have been installed."
                );
                _webSocketServer.BroadcastToEndpointAsync("/install", completionMessage).GetAwaiter().GetResult();
            }
            else
            {
                _logger.Info("No files selected for installation.");

                if (deleteUnselected)
                {
                    foreach (var extractedFile in e.ExtractedFilePaths)
                    {
                        TryDeleteFile(extractedFile);
                    }
                }

                var completionMessage = WebSocketMessage.CreateStatus(
                    taskId,
                    WebSocketMessageStatus.Completed,
                    "No files were selected for installation."
                );
                _webSocketServer.BroadcastToEndpointAsync("/install", completionMessage).GetAwaiter().GetResult();
            }
        }
        else
        {
            // Install all extracted files scenario
            var message = WebSocketMessage.CreateStatus(
                taskId,
                WebSocketMessageStatus.InProgress,
                $"Installing all extracted files from {e.ArchiveFileName}"
            );
            _webSocketServer.BroadcastToEndpointAsync("/status", message).GetAwaiter().GetResult();

            foreach (var extractedFilePath in e.ExtractedFilePaths)
            {
                _modHandlerService.HandleFileAsync(extractedFilePath).GetAwaiter().GetResult();
            }

            var completionMessage = WebSocketMessage.CreateStatus(
                taskId,
                WebSocketMessageStatus.Completed,
                $"All files from {e.ArchiveFileName} have been installed."
            );
            _webSocketServer.BroadcastToEndpointAsync("/status", completionMessage).GetAwaiter().GetResult();
        }
    }
    
    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                _logger.Info("Deleting file: {Path}", filePath);
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error deleting file: {Path}", filePath);
        }
    }

    private List<string> WaitForUserSelection(string taskId)
    {
        var tcs = new TaskCompletionSource<List<string>>();

        // Add the TaskCompletionSource to the dictionary
        _pendingSelections[taskId] = tcs;

        // Wait for the user's selection or a timeout
        if (tcs.Task.Wait(TimeSpan.FromMinutes(5)))
        {
            _pendingSelections.TryRemove(taskId, out _);
            return tcs.Task.Result;
        }
        else
        {
            _logger.Warn("Timeout waiting for user selection for task {TaskId}", taskId);
            _pendingSelections.TryRemove(taskId, out _);
            return new List<string>();
        }
    }

    private void OnWebSocketMessageReceived(object? sender, WebSocketMessageEventArgs e)
    {
        if (e.Endpoint == "/install" &&
            e.Message.Type == WebSocketMessageType.Status &&
            e.Message.Status == "user_selection")
        {
            if (_pendingSelections.TryGetValue(e.Message.TaskId, out var tcs))
            {
                var selectedFiles = JsonConvert.DeserializeObject<List<string>>(e.Message.Message);
                tcs.SetResult(selectedFiles ?? new List<string>());
            }
            else
            {
                _logger.Warn("Received user selection for unknown task {TaskId}", e.Message.TaskId);
            }
        }
    }

    public void Dispose()
    {
        DisposeFileWatcher();

        _configurationService.ConfigurationChanged -= OnConfigurationChanged;
        _webSocketServer.MessageReceived -= OnWebSocketMessageReceived;
    }
}