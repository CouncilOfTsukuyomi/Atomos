
using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Atomos.UI.Extensions;
using Atomos.UI.Interfaces;
using Atomos.UI.Models;
using Atomos.UI.Services;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommonLib.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using ReactiveUI;
using SharedResources;
using Notification = Atomos.UI.Models.Notification;
using Timer = System.Timers.Timer;

namespace Atomos.UI.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationService _notificationService;
    private readonly IWebSocketClient _webSocketClient;
    private readonly ISoundManagerService _soundManagerService;
    private readonly IConfigurationListener _configurationListener;
    private readonly IConfigurationService _configurationService;
    private readonly ITaskbarFlashService _taskbarFlashService;
    
    private Timer? _updateCheckTimer;
    private string _currentVersion = string.Empty;

    private ViewModelBase _currentPage = null!;
    private MenuItem _selectedMenuItem = null!;
    private Size _windowSize = new Size(800, 600);
    private Bitmap? _appLogoSource;

    private PluginSettingsViewModel? _pluginSettingsViewModel;
    public PluginSettingsViewModel? PluginSettingsViewModel
    {
        get => _pluginSettingsViewModel;
        set => this.RaiseAndSetIfChanged(ref _pluginSettingsViewModel, value);
    }

    public Bitmap? AppLogoSource
    {
        get => _appLogoSource;
        private set => this.RaiseAndSetIfChanged(ref _appLogoSource, value);
    }
    
    private bool _isCheckingForUpdates;
    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        set => this.RaiseAndSetIfChanged(ref _isCheckingForUpdates, value);
    }

    public ObservableCollection<MenuItem> MenuItems { get; }
    public ObservableCollection<Notification> Notifications =>
        (_notificationService as NotificationService)?.Notifications ?? new();

    public InstallViewModel InstallViewModel { get; }
    public SentryPromptViewModel SentryPromptViewModel { get; }
    public NotificationHubViewModel NotificationHubViewModel { get; }
    public UpdatePromptViewModel UpdatePromptViewModel { get; }

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
    public ICommand NavigateToAboutCommand { get; }

    public MainWindowViewModel(
        IServiceProvider serviceProvider,
        INotificationService notificationService,
        IWebSocketClient webSocketClient,
        int port,
        IConfigurationListener configurationListener,
        ISoundManagerService soundManagerService,
        IConfigurationService configurationService,
        ITaskbarFlashService taskbarFlashService,
        IUpdateService updateService,
        IRunUpdater runUpdater)
    {
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
        _webSocketClient = webSocketClient;
        _configurationListener = configurationListener;
        _soundManagerService = soundManagerService;
        _configurationService = configurationService;
        _taskbarFlashService = taskbarFlashService;
        
        // Load app logo
        LoadAppLogo();
        
        // Get current version
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        _currentVersion = version == null ? "Local Build" : $"{version.Major}.{version.Minor}.{version.Build}";
        _logger.Info("Application version determined: {Version}", _currentVersion);
        
        // Check the configuration to see if Sentry is enabled at startup
        if ((bool)_configurationService.ReturnConfigValue(c => c.Common.EnableSentry))
        {
            _logger.Info("Enabling Sentry");
            DependencyInjection.EnableSentryLogging();
        }
        
        // Check if debug logs is enabled
        if ((bool) _configurationService.ReturnConfigValue(c => c.AdvancedOptions.EnableDebugLogs))
        {
            _logger.Info("Enabling debug logs");
            DependencyInjection.EnableDebugLogging();
        }

        // Create UpdatePromptViewModel
        UpdatePromptViewModel = new UpdatePromptViewModel(updateService, runUpdater);
        _logger.Info("UpdatePromptViewModel created successfully");

        // Create and manage your SentryPromptViewModel
        SentryPromptViewModel = new SentryPromptViewModel(_configurationService, _webSocketClient)
        {
            IsVisible = false
        };

        var userHasChosenSentry = (bool)_configurationService.ReturnConfigValue(c => c.Common.UserChoseSentry);
        if (!userHasChosenSentry)
        {
            SentryPromptViewModel.IsVisible = true;
        }

        // Create NotificationHubViewModel
        NotificationHubViewModel = new NotificationHubViewModel(_notificationService, _configurationService);

        var app = Application.Current;

        var homeViewModel = _serviceProvider.GetRequiredService<HomeViewModel>();
        var modsViewModel = _serviceProvider.GetRequiredService<ModsViewModel>();
        var pluginsViewModel = _serviceProvider.GetRequiredService<PluginViewModel>();
        var pluginDataViewModel = _serviceProvider.GetRequiredService<PluginDataViewModel>();

        // Subscribe to plugin settings requests
        pluginsViewModel.PluginSettingsRequested += OnPluginSettingsRequested;

        MenuItems = new ObservableCollection<MenuItem>
        {
            new MenuItem(
                "Home",
                app?.Resources["HomeIcon"] as StreamGeometry ?? StreamGeometry.Parse("M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z"),
                homeViewModel
            ),
            new MenuItem(
                "Mods",
                app?.Resources["ModsIcon"] as StreamGeometry ?? StreamGeometry.Parse("M19 3C20.1 3 21 3.9 21 5V19C21 20.1 20.1 21 19 21H5C3.9 21 3 20.1 3 19V5C3 3.9 3.9 3 5 3H19M5 5V19H19V5H5M7 7H17V9H7V7M7 11H17V13H7V11M7 15H14V17H7V15Z"),
                modsViewModel
            ),
            new MenuItem(
                "Plugins",
                app?.Resources["PluginsIcon"] as StreamGeometry ?? StreamGeometry.Parse("M12 2L2 7V10C2 16 6 20.5 12 22C18 20.5 22 16 22 10V7L12 2M10 17L6 13L7.41 11.59L10 14.17L16.59 7.58L18 9L10 17Z"),
                pluginsViewModel
            ),
            new MenuItem(
                "Plugin Data",
                app?.Resources["DataIcon"] as StreamGeometry ?? StreamGeometry.Parse("M19,3H5C3.9,3 3,3.9 3,5V19C3,20.1 3.9,21 5,21H19C20.1,21 21,20.1 21,19V5C21,3.9 20.1,3 19,3M9,17H7V10H9V17M13,17H11V7H13V17M17,17H15V13H17V17Z"),
                pluginDataViewModel
            )
        };

        NavigateToSettingsCommand = ReactiveCommand.Create(() =>
        {
            SelectedMenuItem = null;
            CurrentPage = ActivatorUtilities.CreateInstance<SettingsViewModel>(_serviceProvider);
        });

        NavigateToAboutCommand = ReactiveCommand.Create(() =>
        {
            SelectedMenuItem = null;
            CurrentPage = new AboutViewModel();
        });

        _selectedMenuItem = MenuItems[0];
        _currentPage = _selectedMenuItem.ViewModel;

        InstallViewModel = new InstallViewModel(_webSocketClient, _soundManagerService, _taskbarFlashService);

        _ = InitializeAsync(port);
    }

    private void LoadAppLogo()
    {
        try
        {
            var logoStream = ResourceLoader.GetResourceStream("Purple_arrow_cat_image.png");
            if (logoStream != null)
            {
                AppLogoSource = new Bitmap(logoStream);
                _logger.Debug("App logo loaded successfully");
            }
            else
            {
                _logger.Warn("App logo resource not found");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load app logo");
        }
    }

    private async Task InitializeAsync(int port)
    {
        _logger.Debug("Starting InitializeAsync with port: {Port}", port);
        
        StartUpdateCheckTimer();
        
        _logger.Debug("Starting initial update check");
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.Debug("About to call CheckForUpdatesAsync from Task.Run");
                await CheckForUpdatesAsync();
                _logger.Debug("Initial update check completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Initial update check failed");
            }
        });
        
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await InitializeWebSocketConnectionWithTimeout(port, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize WebSocket connection, but continuing without it");
            }
        });
        
        _logger.Debug("InitializeAsync completed");
    }

    private async Task InitializeWebSocketConnectionWithTimeout(int port, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Debug("Attempting WebSocket connection to port {Port}", port);
            
            var connectTask = Task.Run(() => _webSocketClient.ConnectAsync(port), cancellationToken);
            await connectTask;
            
            _logger.Info("WebSocket connection established successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("WebSocket connection timed out after 10 seconds");
            await _notificationService.ShowNotification(
                "Connection timeout",
                "Failed to connect to background service within 10 seconds."
            );
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

    private void StartUpdateCheckTimer()
    {
        _logger.Debug("Starting update check timer (5 minute intervals)");
    
        _updateCheckTimer = new Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
        _updateCheckTimer.Elapsed += async (sender, e) =>
        {
            try
            {
                _logger.Debug("Timer elapsed - starting scheduled update check");
                await CheckForUpdatesAsync();
                _logger.Debug("Scheduled update check completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during scheduled update check");
            }
        };
        _updateCheckTimer.AutoReset = true;
        _updateCheckTimer.Start();
    
        _logger.Debug("Update check timer started successfully");
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            _logger.Debug("CheckForUpdatesAsync started. UpdatePromptViewModel.IsVisible: {IsVisible}", UpdatePromptViewModel.IsVisible);
            
            if (!UpdatePromptViewModel.IsVisible)
            {
                IsCheckingForUpdates = true;
                _logger.Debug("Checking for updates... Current version: {Version}", _currentVersion);
                await UpdatePromptViewModel.CheckForUpdatesAsync(_currentVersion);
                _logger.Debug("Update check call completed. UpdatePromptViewModel.IsVisible: {IsVisible}", UpdatePromptViewModel.IsVisible);
            }
            else
            {
                _logger.Debug("Skipping update check - update prompt is already visible");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for updates");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }


    private void OnPluginSettingsRequested(PluginSettingsViewModel settingsViewModel)
    {
        _logger.Info("Plugin settings requested for {PluginId}", settingsViewModel.Plugin.PluginId);
    
        // Subscribe to the closed event
        settingsViewModel.Closed += () => {
            _logger.Info("Plugin settings dialog closed, clearing PluginSettingsViewModel");
            PluginSettingsViewModel = null;
        };
    
        // Show the dialog and assign it
        settingsViewModel.Show();
        PluginSettingsViewModel = settingsViewModel;
    }
    
    public void Dispose()
    {
        _updateCheckTimer?.Stop();
        _updateCheckTimer?.Dispose();
        SentryPromptViewModel?.Dispose();
    }
}