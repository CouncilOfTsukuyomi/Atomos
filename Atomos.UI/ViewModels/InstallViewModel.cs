
using System;
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

    private string _currentTaskId;
    private bool _isSelectionVisible;
    private bool _areAllSelected;

    private StandaloneInstallWindow _standaloneWindow;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    /// <summary>
    /// Whether the file selection UI is visible. If set to false, closes the standalone window if it's open.
    /// </summary>
    public bool IsSelectionVisible
    {
        get => _isSelectionVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSelectionVisible, value);

            // If the user interface is no longer visible, close the standalone window
            if (!value && _standaloneWindow != null)
            {
                _standaloneWindow.Close();
                _standaloneWindow = null;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether all files are selected. This is used for the Select All functionality.
    /// </summary>
    public bool AreAllSelected
    {
        get => _areAllSelected;
        set
        {
            this.RaiseAndSetIfChanged(ref _areAllSelected, value);
            UpdateAllFilesSelection(value);
        }
    }

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

        InstallCommand = ReactiveCommand.CreateFromTask(ExecuteInstallCommand);
        CancelCommand = ReactiveCommand.CreateFromTask(ExecuteCancelCommand);
        SelectAllCommand = ReactiveCommand.Create(ExecuteSelectAllCommand);

        _webSocketClient.FileSelectionRequested += OnFileSelectionRequested;
        
        Files.CollectionChanged += (sender, args) => UpdateAreAllSelectedProperty();
    }

    private void OnFileSelectionRequested(object sender, FileSelectionRequestedEventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            _currentTaskId = e.TaskId;
            Files.Clear();

            foreach (var file in e.AvailableFiles)
            {
                var fileName = Path.GetFileName(file);
                var fileItem = new FileItemViewModel
                {
                    FileName = fileName,
                    FilePath = file,
                    IsSelected = false
                };

                // Subscribe to PropertyChanged to update AreAllSelected when individual files change
                fileItem.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(FileItemViewModel.IsSelected))
                    {
                        UpdateAreAllSelectedProperty();
                    }
                };

                Files.Add(fileItem);
                _logger.Info("Added file {FileName}", fileName);
            }

            _logger.Info("Selected {FileCount} files", Files.Count);
            UpdateAreAllSelectedProperty();
            IsSelectionVisible = true;

            // Flash the taskbar to get user attention
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

    private async Task ExecuteInstallCommand()
    {
        var selectedFiles = Files
            .Where(f => f.IsSelected)
            .Select(f => f.FilePath)
            .ToList();

        var responseMessage = new WebSocketMessage
        {
            Type = WebSocketMessageType.Status,
            TaskId = _currentTaskId,
            Status = "user_selection",
            Progress = 0,
            Message = JsonConvert.SerializeObject(selectedFiles)
        };

        await _webSocketClient.SendMessageAsync(responseMessage, "/install");
        IsSelectionVisible = false;

        // Stop flashing when user makes a choice
        _taskbarFlashService.StopFlashing();

        _logger.Info("User selected files sent: {SelectedFiles}", selectedFiles);
    }

    private async Task ExecuteCancelCommand()
    {
        IsSelectionVisible = false;
        _logger.Info("User canceled the file selection.");

        // Stop flashing when user cancels
        _taskbarFlashService.StopFlashing();

        var responseMessage = new WebSocketMessage
        {
            Type = WebSocketMessageType.Status,
            TaskId = _currentTaskId,
            Status = "user_selection",
            Progress = 0,
            Message = JsonConvert.SerializeObject(new List<string>())
        };

        await _webSocketClient.SendMessageAsync(responseMessage, "/install");
    }

    public void Dispose()
    {
        // Unsubscribe from the event to avoid memory leaks
        _webSocketClient.FileSelectionRequested -= OnFileSelectionRequested;

        // Close the standalone window if it exists
        if (_standaloneWindow != null)
        {
            _standaloneWindow.Close();
            _standaloneWindow = null;
        }
    }
}