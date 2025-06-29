using System.Collections.Concurrent;
using Atomos.BackgroundWorker.Events;
using Atomos.BackgroundWorker.Interfaces;
using CommonLib.Events;
using CommonLib.Interfaces;
using CommonLib.Models;
using Newtonsoft.Json;
using NLog;
using Atomos.FileMonitor.Events;
using Atomos.FileMonitor.Interfaces;
using Atomos.FileMonitor.Models;

namespace Atomos.BackgroundWorker.Services;

public class FileWatcherService : IFileWatcherService, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly IWebSocketServer _webSocketServer;
    private readonly IServiceProvider _serviceProvider;
    private readonly IModHandlerService _modHandlerService;

    private IFileWatcher? _fileWatcher;
    private bool _eventsSubscribed;

    // Dictionary to track pending user selections for archive files
    private readonly ConcurrentDictionary<string, TaskCompletionSource<List<string>>> _pendingArchiveSelections = new();
    // Dictionary to track archive paths for tasks
    private readonly ConcurrentDictionary<string, string> _taskArchivePaths = new();

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
                _fileWatcher.ArchiveContentsInspected += OnArchiveContentsInspected;
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
                _fileWatcher.ArchiveContentsInspected -= OnArchiveContentsInspected;
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

    private async void OnArchiveContentsInspected(object? sender, ArchiveContentsInspectedEventArgs e)
    {
        _logger.Info("Archive contents inspected: {ArchivePath} - {FileCount} files found", 
            e.ArchivePath, e.Files.Count);
        
        _taskArchivePaths[e.TaskId] = e.ArchivePath;

        var installAll = (bool)_configurationService.ReturnConfigValue(config => config.BackgroundWorker.InstallAll);

        if (installAll)
        {
            var modFiles = e.Files.Where(f => f.IsModFile).Select(f => f.RelativePath).ToList();
            
            if (modFiles.Any())
            {
                var fileProcessor = _serviceProvider.GetRequiredService<IFileProcessor>();
                await fileProcessor.ExtractSelectedFilesAsync(e.ArchivePath, modFiles, CancellationToken.None, e.TaskId);
            }
            else
            {
                _logger.Info("No mod files found in archive to auto-extract: {ArchivePath}", e.ArchivePath);
            }
        }
        else
        {
            var archiveContentsJson = JsonConvert.SerializeObject(e.Files);

            var message = new WebSocketMessage
            {
                Type = WebSocketMessageType.Status,
                TaskId = e.TaskId,
                Status = "select_archive_files",
                Progress = 0,
                Message = archiveContentsJson
            };

            try
            {
                await _webSocketServer.BroadcastToEndpointAsync("/install", message);
                
                _ = Task.Run(async () => await WaitForArchiveFileSelection(e.TaskId, e.ArchivePath));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send archive contents to user");
            }
        }
    }

    private async Task WaitForArchiveFileSelection(string taskId, string archivePath)
    {
        try
        {
            var selectedFiles = await WaitForUserArchiveSelection(taskId);

            if (selectedFiles != null && selectedFiles.Any())
            {
                _logger.Info("User selected {Count} files from archive: {ArchivePath}", 
                    selectedFiles.Count, archivePath);

                var fileProcessor = _serviceProvider.GetRequiredService<IFileProcessor>();
                await fileProcessor.ExtractSelectedFilesAsync(archivePath, selectedFiles, CancellationToken.None, taskId);
            }
            else
            {
                _logger.Info("No files selected for extraction from archive: {ArchivePath}", archivePath);
                
                var completionMessage = WebSocketMessage.CreateStatus(
                    taskId,
                    WebSocketMessageStatus.Completed,
                    "No files were selected for extraction."
                );
                await _webSocketServer.BroadcastToEndpointAsync("/status", completionMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during archive file selection process for task {TaskId}", taskId);
        }
        finally
        {
            _taskArchivePaths.TryRemove(taskId, out _);
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
        
        var message = WebSocketMessage.CreateStatus(
            taskId,
            WebSocketMessageStatus.InProgress,
            $"Installing extracted files from {e.ArchiveFileName}"
        );
        _webSocketServer.BroadcastToEndpointAsync("/status", message).GetAwaiter().GetResult();

        foreach (var extractedFilePath in e.ExtractedFilePaths)
        {
            _modHandlerService.HandleFileAsync(extractedFilePath).GetAwaiter().GetResult();
        }

        var completionMessage = WebSocketMessage.CreateStatus(
            taskId,
            WebSocketMessageStatus.Completed,
            $"All extracted files from {e.ArchiveFileName} have been installed."
        );
        _webSocketServer.BroadcastToEndpointAsync("/status", completionMessage).GetAwaiter().GetResult();
    }

    private async Task<List<string>> WaitForUserArchiveSelection(string taskId)
    {
        var tcs = new TaskCompletionSource<List<string>>();
        
        _pendingArchiveSelections[taskId] = tcs;

        try
        {
            // Wait for the user's selection or a timeout
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }
            else
            {
                _logger.Warn("Timeout waiting for user archive selection for task {TaskId}", taskId);
                return new List<string>();
            }
        }
        finally
        {
            _pendingArchiveSelections.TryRemove(taskId, out _);
        }
    }

    private void OnWebSocketMessageReceived(object? sender, WebSocketMessageEventArgs e)
    {
        if (e.Endpoint == "/extract" &&
            e.Message.Type == WebSocketMessageType.Status &&
            e.Message.Status == "user_archive_selection")
        {
            if (_pendingArchiveSelections.TryGetValue(e.Message.TaskId, out var tcs))
            {
                var selectedFiles = JsonConvert.DeserializeObject<List<string>>(e.Message.Message);
                tcs.SetResult(selectedFiles ?? new List<string>());
            }
            else
            {
                _logger.Warn("Received user archive selection for unknown task {TaskId}", e.Message.TaskId);
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