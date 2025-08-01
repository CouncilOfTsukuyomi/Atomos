using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.Timers;
using Atomos.UI.Events;
using Atomos.UI.Interfaces;
using Atomos.UI.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommonLib.Enums;
using CommonLib.Interfaces;
using CommonLib.Models;
using Newtonsoft.Json;
using NLog;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class InstallViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IWebSocketClient _webSocketClient;
    private readonly ISoundManagerService _soundManagerService;
    private readonly ITaskbarFlashService _taskbarFlashService;

    private readonly ConcurrentQueue<FileSelectionRequest> _selectionQueue = new();
    private FileSelectionRequest _currentRequest;
    private bool _isProcessingQueue;
    
    private bool _isSelectionVisible;
    private bool _areAllSelected;
    private bool _showSelectAll;
    private int _timeoutSeconds = 300;
    private string _timeoutMessage = "5:00";
    private bool _isTimeoutWarning;

    private StandaloneInstallWindow _standaloneWindow;
    private Timer _timeoutTimer;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();
    
    public bool IsSelectionVisible
    {
        get => _isSelectionVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSelectionVisible, value);

            if (!value && _standaloneWindow != null)
            {
                _standaloneWindow.Close();
                _standaloneWindow = null;
            }
        }
    }
    
    public bool AreAllSelected
    {
        get => _areAllSelected;
        set
        {
            this.RaiseAndSetIfChanged(ref _areAllSelected, value);
            UpdateAllFilesSelection(value);
        }
    }
    
    public bool ShowSelectAll
    {
        get => _showSelectAll;
        set => this.RaiseAndSetIfChanged(ref _showSelectAll, value);
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeoutSeconds, value);
    }

    public string TimeoutMessage
    {
        get => _timeoutMessage;
        set => this.RaiseAndSetIfChanged(ref _timeoutMessage, value);
    }

    public bool IsTimeoutWarning
    {
        get => _isTimeoutWarning;
        set => this.RaiseAndSetIfChanged(ref _isTimeoutWarning, value);
    }
    
    public bool HasSelectedFiles => Files.Any(f => f.IsSelected);

    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<FileItemViewModel, Unit> ToggleFileSelectionCommand { get; }

    public InstallViewModel(
        IWebSocketClient webSocketClient,
        ISoundManagerService soundManagerService,
        ITaskbarFlashService taskbarFlashService)
    {
        _webSocketClient = webSocketClient;
        _soundManagerService = soundManagerService;
        _taskbarFlashService = taskbarFlashService;
        
        var canInstall = this.WhenAnyValue(x => x.HasSelectedFiles);
        InstallCommand = ReactiveCommand.CreateFromTask(ExecuteInstallCommand, canInstall);
        CancelCommand = ReactiveCommand.CreateFromTask(ExecuteCancelCommand);
        SelectAllCommand = ReactiveCommand.Create(ExecuteSelectAllCommand);
        ToggleFileSelectionCommand = ReactiveCommand.Create<FileItemViewModel>(ExecuteToggleFileSelection);

        _webSocketClient.FileSelectionRequested += OnFileSelectionRequested;
        
        Files.CollectionChanged += (sender, args) => 
        {
            UpdateAreAllSelectedProperty();
            UpdateShowSelectAllProperty();
            UpdateHasSelectedFilesProperty();
        };

        InitializeTimeoutTimer();
    }

    private void InitializeTimeoutTimer()
    {
        _timeoutTimer = new Timer(1000);
        _timeoutTimer.Elapsed += OnTimeoutTimerElapsed;
    }

    private async void OnTimeoutTimerElapsed(object sender, ElapsedEventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TimeoutSeconds--;
            
            var minutes = TimeoutSeconds / 60;
            var seconds = TimeoutSeconds % 60;
            TimeoutMessage = $"{minutes}:{seconds:D2}";
            
            IsTimeoutWarning = TimeoutSeconds <= 60;
            
            if (TimeoutSeconds <= 0)
            {
                _timeoutTimer?.Stop();
                _logger.Warn("File selection timeout reached for task {TaskId}", _currentRequest?.TaskId);
                
                _ = Task.Run(async () =>
                {
                    await ExecuteCancelCommand();
                });
            }
        });
    }

    private void StartTimeoutTimer()
    {
        TimeoutSeconds = 300;
        var minutes = TimeoutSeconds / 60;
        var seconds = TimeoutSeconds % 60;
        TimeoutMessage = $"{minutes}:{seconds:D2}";
        IsTimeoutWarning = false;
        
        _timeoutTimer?.Start();
        _logger.Info("Started 5-minute timeout timer for file selection");
    }

    private void StopTimeoutTimer()
    {
        _timeoutTimer?.Stop();
        IsTimeoutWarning = false;
        _logger.Debug("Stopped timeout timer");
    }

    private void ExecuteToggleFileSelection(FileItemViewModel fileItem)
    {
        if (fileItem != null)
        {
            fileItem.IsSelected = !fileItem.IsSelected;
            _logger.Debug("Toggled file selection for {FileName}: {IsSelected}", fileItem.FileName, fileItem.IsSelected);
        }
    }

    private void OnFileSelectionRequested(object sender, FileSelectionRequestedEventArgs e)
    {
        var request = new FileSelectionRequest
        {
            TaskId = e.TaskId,
            AvailableFiles = e.AvailableFiles.ToList()
        };

        _selectionQueue.Enqueue(request);
        _logger.Info("Queued file selection request for task {TaskId}. Queue size: {QueueSize}", 
            e.TaskId, _selectionQueue.Count);

        _ = Task.Run(ProcessQueueAsync);
    }

    private async Task ProcessQueueAsync()
    {
        if (_isProcessingQueue)
        {
            return;
        }

        _isProcessingQueue = true;

        try
        {
            while (_selectionQueue.TryDequeue(out var request))
            {
                await ProcessFileSelectionRequest(request);
            }
        }
        finally
        {
            _isProcessingQueue = false;
        }
    }

    private async Task ProcessFileSelectionRequest(FileSelectionRequest request)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _currentRequest = request;
            Files.Clear();

            var commonRoot = FindCommonRootPath(request.AvailableFiles);

            foreach (var file in request.AvailableFiles)
            {
                var displayPath = GetRelativeDisplayPath(file, commonRoot);
                
                var fileItem = new FileItemViewModel
                {
                    FileName = displayPath,
                    FilePath = file,
                    IsSelected = false
                };

                fileItem.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(FileItemViewModel.IsSelected))
                    {
                        UpdateAreAllSelectedProperty();
                        UpdateHasSelectedFilesProperty();
                    }
                };

                Files.Add(fileItem);
                _logger.Info("Added file {DisplayPath} (full: {FullPath}) for task {TaskId}", 
                    displayPath, file, request.TaskId);
            }

            _logger.Info("Processing file selection for task {TaskId} with {FileCount} files", 
                request.TaskId, Files.Count);
            
            UpdateAreAllSelectedProperty();
            UpdateShowSelectAllProperty();
            UpdateHasSelectedFilesProperty();
            IsSelectionVisible = true;
            
            StartTimeoutTimer();

            _taskbarFlashService.FlashTaskbar();

            await _soundManagerService.PlaySoundAsync(
                SoundType.GeneralChime,
                volume: 1.0f
            );
            
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (!desktop.MainWindow.IsVisible)
                {
                    _standaloneWindow = new StandaloneInstallWindow(this);
                    _standaloneWindow.Show();
                }
            }
        });
        
        await WaitForUserInteraction();
    }

    private string FindCommonRootPath(List<string> filePaths)
    {
        if (filePaths.Count == 0) return string.Empty;
        if (filePaths.Count == 1) return Path.GetDirectoryName(filePaths[0]) ?? string.Empty;

        var firstPath = filePaths[0];
        var commonPath = Path.GetDirectoryName(firstPath) ?? string.Empty;

        foreach (var path in filePaths.Skip(1))
        {
            var currentDir = Path.GetDirectoryName(path) ?? string.Empty;
            commonPath = GetCommonPath(commonPath, currentDir);
            if (string.IsNullOrEmpty(commonPath)) break;
        }

        return commonPath;
    }

    private string GetCommonPath(string path1, string path2)
    {
        var parts1 = path1.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parts2 = path2.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var commonParts = new List<string>();
        var minLength = Math.Min(parts1.Length, parts2.Length);

        for (int i = 0; i < minLength; i++)
        {
            if (string.Equals(parts1[i], parts2[i], StringComparison.OrdinalIgnoreCase))
            {
                commonParts.Add(parts1[i]);
            }
            else
            {
                break;
            }
        }

        return string.Join(Path.DirectorySeparatorChar.ToString(), commonParts);
    }

    private string GetRelativeDisplayPath(string fullPath, string commonRoot)
    {
        if (string.IsNullOrEmpty(commonRoot))
        {
            return fullPath;
        }

        var relativePath = Path.GetRelativePath(commonRoot, fullPath);
        
        if (relativePath.StartsWith(".."))
        {
            return fullPath;
        }

        return relativePath;
    }

    private TaskCompletionSource<bool> _userInteractionTcs;

    private async Task WaitForUserInteraction()
    {
        _userInteractionTcs = new TaskCompletionSource<bool>();
        await _userInteractionTcs.Task;
    }

    private void CompleteUserInteraction()
    {
        StopTimeoutTimer();
        _userInteractionTcs?.SetResult(true);
    }

    private void ExecuteSelectAllCommand()
    {
        AreAllSelected = !AreAllSelected;
        _logger.Info("Select all toggled: {AreAllSelected}", AreAllSelected);
    }

    private void UpdateAllFilesSelection(bool isSelected)
    {
        foreach (var file in Files)
        {
            file.IsSelected = isSelected;
        }
    }

    private void UpdateAreAllSelectedProperty()
    {
        var newAreAllSelected = Files.Count > 0 && Files.All(f => f.IsSelected);
        if (_areAllSelected != newAreAllSelected)
        {
            _areAllSelected = newAreAllSelected;
            this.RaisePropertyChanged(nameof(AreAllSelected));
        }
    }

    private void UpdateShowSelectAllProperty()
    {
        ShowSelectAll = Files.Count >= 3;
    }

    private void UpdateHasSelectedFilesProperty()
    {
        this.RaisePropertyChanged(nameof(HasSelectedFiles));
    }

    private async Task ExecuteInstallCommand()
    {
        if (_currentRequest == null)
        {
            _logger.Warn("No current request to process");
            return;
        }

        var selectedFiles = Files
            .Where(f => f.IsSelected)
            .Select(f => f.FilePath)
            .ToList();
        
        var responseMessage = new WebSocketMessage
        {
            Type = WebSocketMessageType.Status,
            TaskId = _currentRequest.TaskId,
            Status = "user_archive_selection",
            Progress = 0,
            Message = JsonConvert.SerializeObject(selectedFiles)
        };

        await _webSocketClient.SendMessageAsync(responseMessage, "/extract");
        IsSelectionVisible = false;
        
        _taskbarFlashService.StopFlashing();

        _logger.Info("User selected archive files sent for task {TaskId}: {SelectedFiles}", 
            _currentRequest.TaskId, selectedFiles);

        CompleteUserInteraction();
    }

    private async Task ExecuteCancelCommand()
    {
        if (_currentRequest == null)
        {
            _logger.Warn("No current request to cancel");
            return;
        }

        IsSelectionVisible = false;
        _logger.Info("User canceled the archive file selection for task {TaskId}", _currentRequest.TaskId);
        
        _taskbarFlashService.StopFlashing();
        
        var responseMessage = new WebSocketMessage
        {
            Type = WebSocketMessageType.Status,
            TaskId = _currentRequest.TaskId,
            Status = "user_archive_selection",
            Progress = 0,
            Message = JsonConvert.SerializeObject(new List<string>())
        };

        await _webSocketClient.SendMessageAsync(responseMessage, "/extract");
        
        CompleteUserInteraction();
    }

    public void Dispose()
    {
        _webSocketClient.FileSelectionRequested -= OnFileSelectionRequested;
        
        StopTimeoutTimer();
        _timeoutTimer?.Dispose();
        
        if (_standaloneWindow != null)
        {
            _standaloneWindow.Close();
            _standaloneWindow = null;
        }
    }

    private class FileSelectionRequest
    {
        public string TaskId { get; set; }
        public List<string> AvailableFiles { get; set; }
    }
}