using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Atomos.UI.Extensions;
using Atomos.UI.Interfaces;
using Atomos.UI.Models;
using Atomos.UI.Services;
using Avalonia;
using Avalonia.Media;
using CommonLib.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using ReactiveUI;
using Notification = Atomos.UI.Models.Notification;

namespace Atomos.UI.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationService _notificationService;
    private readonly IWebSocketClient _webSocketClient;
    private readonly ISoundManagerService _soundManagerService;
    private readonly IConfigurationListener _configurationListener;
    private readonly IConfigurationService _configurationService;
    private readonly ITaskbarFlashService _taskbarFlashService;

    private ViewModelBase _currentPage = null!;
    private MenuItem _selectedMenuItem = null!;
    private Size _windowSize = new Size(800, 600);

    public ObservableCollection<MenuItem> MenuItems { get; }
    public ObservableCollection<Notification> Notifications =>
        (_notificationService as NotificationService)?.Notifications ?? new();

    public InstallViewModel InstallViewModel { get; }
    public SentryPromptViewModel SentryPromptViewModel { get; }
    public NotificationHubViewModel NotificationHubViewModel { get; }

    public MenuItem SelectedMenuItem
    {
        get => _selectedMenuItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMenuItem, value);
            if (value != null)
            {
                CurrentPage = value.ViewModel;
            }
        }
    }

    public ViewModelBase CurrentPage
    {
        get => _currentPage;
        set => this.RaiseAndSetIfChanged(ref _currentPage, value);
    }

    public Size WindowSize
    {
        get => _windowSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _windowSize, value);
            NotificationHubViewModel?.SetParentBounds(value);
            _logger.Debug("Window size updated to: {Width} x {Height}", value.Width, value.Height);
        }
    }

    public ICommand NavigateToSettingsCommand { get; }

    public MainWindowViewModel(
        IServiceProvider serviceProvider,
        INotificationService notificationService,
        IWebSocketClient webSocketClient,
        int port,
        IConfigurationListener configurationListener,
        ISoundManagerService soundManagerService,
        IConfigurationService configurationService,
        ITaskbarFlashService taskbarFlashService)
    {
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
        _webSocketClient = webSocketClient;
        _configurationListener = configurationListener;
        _soundManagerService = soundManagerService;
        _configurationService = configurationService;
        _taskbarFlashService = taskbarFlashService;
        
        // Check the configuration to see if Sentry is enabled at startup
        if ((bool)_configurationService.ReturnConfigValue(c => c.Common.EnableSentry))
        {
            _logger.Info("Enabling Sentry");
            DependencyInjection.EnableSentryLogging();
        }

        // Create and manage your SentryPromptViewModel
        SentryPromptViewModel = new SentryPromptViewModel(_configurationService, _webSocketClient)
        {
            // Set this to false initially; you can display it if the user hasn't chosen yet
            IsVisible = false
        };

        var userHasChosenSentry = (bool)_configurationService.ReturnConfigValue(c => c.Common.UserChoseSentry);
        if (!userHasChosenSentry)
        {
            // If the user has never made a choice, show the prompt overlay
            SentryPromptViewModel.IsVisible = true;
        }

        // Create NotificationHubViewModel
        NotificationHubViewModel = new NotificationHubViewModel(_notificationService, _configurationService);

        var app = Application.Current;

        var homeViewModel = _serviceProvider.GetRequiredService<HomeViewModel>();
        var modsViewModel = _serviceProvider.GetRequiredService<ModsViewModel>();

        MenuItems = new ObservableCollection<MenuItem>
        {
            new MenuItem(
                "Home",
                app?.Resources["HomeIcon"] as StreamGeometry ?? StreamGeometry.Parse(""),
                homeViewModel
            ),
            new MenuItem(
                "Mods",
                app?.Resources["MenuIcon"] as StreamGeometry ?? StreamGeometry.Parse(""),
                modsViewModel
            )
        };

        NavigateToSettingsCommand = ReactiveCommand.Create(() =>
        {
            SelectedMenuItem = null;
            CurrentPage = ActivatorUtilities.CreateInstance<SettingsViewModel>(_serviceProvider);
        });

        _selectedMenuItem = MenuItems[0];
        _currentPage = _selectedMenuItem.ViewModel;

        InstallViewModel = new InstallViewModel(_webSocketClient, _soundManagerService, _taskbarFlashService);

        _ = InitializeWebSocketConnection(port);
    }

    private async Task InitializeWebSocketConnection(int port)
    {
        try
        {
            await Task.Run(() => _webSocketClient.ConnectAsync(port));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize WebSocket connection");
            await _notificationService.ShowNotification(
                "Connection error",
                "Failed to connect to background service."
            );
        }
    }
}