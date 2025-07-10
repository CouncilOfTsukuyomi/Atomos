using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
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

    private StandaloneInstallWindow _standaloneWindow;

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
    
    public bool HasSelectedFiles => Files.Any(f => f.IsSelected);

    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }

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

        _webSocketClient.FileSelectionRequested += OnFileSelectionRequested;
        
        Files.CollectionChanged += (sender, args) => 
        {
            UpdateAreAllSelectedProperty();
            UpdateShowSelectAllProperty();
            UpdateHasSelectedFilesProperty();
        };
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

            foreach (var file in request.AvailableFiles)
            {
                var fileName = Path.GetFileName(file);
                var fileItem = new FileItemViewModel
                {
                    FileName = fileName,
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
                _logger.Info("Added file {FileName} for task {TaskId}", fileName, request.TaskId);
            }

            _logger.Info("Processing file selection for task {TaskId} with {FileCount} files", 
                request.TaskId, Files.Count);
            
            UpdateAreAllSelectedProperty();
            UpdateShowSelectAllProperty();
            UpdateHasSelectedFilesProperty();
            IsSelectionVisible = true;

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

    private TaskCompletionSource<bool> _userInteractionTcs;

    private async Task WaitForUserInteraction()
    {
        _userInteractionTcs = new TaskCompletionSource<bool>();
        await _userInteractionTcs.Task;
    }

    private void CompleteUserInteraction()
    {
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