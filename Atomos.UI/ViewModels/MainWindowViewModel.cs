using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Atomos.UI.Extensions;
using Atomos.UI.Interfaces;
using Atomos.UI.Interfaces.Tutorial;
using Atomos.UI.Models;
using Atomos.UI.Services;
using Atomos.UI.Services.Tutorial;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
    private readonly ITrayIconManager _trayIconManager;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly ITutorialService _tutorialService;
    private readonly IElementHighlightService _elementHighlightService;
    
    private Timer? _updateCheckTimer;
    private string _currentVersion = string.Empty;
    private bool _hasShownFirstRunTutorial = false;
    private bool _isFirstRunTutorial = false;
    private IDisposable? _sentryAcceptSubscription;
    private IDisposable? _sentryDeclineSubscription;

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
        set
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                this.RaiseAndSetIfChanged(ref _isCheckingForUpdates, value);
            }
            else
            {
                Dispatcher.UIThread.Post(() => this.RaiseAndSetIfChanged(ref _isCheckingForUpdates, value));
            }
        }
    }

    public ObservableCollection<MenuItem> MenuItems { get; }
    public ObservableCollection<Notification> Notifications =>
        (_notificationService as NotificationService)?.Notifications ?? new();

    public InstallViewModel InstallViewModel { get; }
    public SentryPromptViewModel SentryPromptViewModel { get; }
    public NotificationHubViewModel NotificationHubViewModel { get; }
    public UpdatePromptViewModel UpdatePromptViewModel { get; }
    public TutorialOverlayViewModel TutorialViewModel { get; }

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
    
    public bool IsBetaBuild { get; private set; }

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
        IRunUpdater runUpdater, 
        ITrayIconManager trayIconManager,
        IUpdateCheckService updateCheckService,
        ITutorialService tutorialService, 
        IElementHighlightService elementHighlightService)
    {
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
        _webSocketClient = webSocketClient;
        _configurationListener = configurationListener;
        _soundManagerService = soundManagerService;
        _configurationService = configurationService;
        _taskbarFlashService = taskbarFlashService;
        _trayIconManager = trayIconManager;
        _updateCheckService = updateCheckService;
        _tutorialService = tutorialService;
        _elementHighlightService = elementHighlightService;

        TutorialViewModel = new TutorialOverlayViewModel(tutorialService, elementHighlightService);
        TutorialViewModel.NavigationRequested += OnTutorialNavigationRequested;
        tutorialService.NavigationRequested += OnTutorialServiceNavigationRequested;
        tutorialService.TabNavigationRequested += OnTutorialTabNavigationRequested;


        LoadAppLogo();
        SetupConfigurationListener();
        
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        _currentVersion = version == null ? "Local Build" : $"{version.Major}.{version.Minor}.{version.Build}";

        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var isBetaBuild = !string.IsNullOrEmpty(informationalVersion) && 
                          (informationalVersion.Contains("-b") || informationalVersion.Contains("beta")) ||
                          _currentVersion.Contains("Local Build") ||
                          System.Diagnostics.Debugger.IsAttached;

        IsBetaBuild = isBetaBuild;
        _logger.Info("Application version determined: {Version} (Beta: {IsBeta})", _currentVersion, isBetaBuild);

        if ((bool)_configurationService.ReturnConfigValue(c => c.Common.EnableSentry))
        {
            _logger.Info("Enabling Sentry");
            DependencyInjection.EnableSentryLogging();
        }
        
        if ((bool) _configurationService.ReturnConfigValue(c => c.AdvancedOptions.EnableDebugLogs))
        {
            _logger.Info("Enabling debug logs");
            DependencyInjection.EnableDebugLogging();
        }

        UpdatePromptViewModel = new UpdatePromptViewModel(updateService, runUpdater);
        _logger.Info("UpdatePromptViewModel created successfully");

        SentryPromptViewModel = new SentryPromptViewModel(_configurationService, _webSocketClient)
        {
            IsVisible = false
        };

        // Subscribe to Sentry prompt completion to trigger tutorial
        _sentryAcceptSubscription = SentryPromptViewModel.AcceptCommand.Subscribe(_ => OnSentryChoiceMade());
        _sentryDeclineSubscription = SentryPromptViewModel.DeclineCommand.Subscribe(_ => OnSentryChoiceMade());

        var userHasChosenSentry = (bool)_configurationService.ReturnConfigValue(c => c.Common.UserChoseSentry);
        if (!userHasChosenSentry)
        {
            SentryPromptViewModel.IsVisible = true;
        }

        NotificationHubViewModel = new NotificationHubViewModel(_notificationService, _configurationService);

        var app = Application.Current;

        var homeViewModel = _serviceProvider.GetRequiredService<HomeViewModel>();
        var modsViewModel = _serviceProvider.GetRequiredService<ModsViewModel>();
        var pluginsViewModel = _serviceProvider.GetRequiredService<PluginViewModel>();
        var pluginDataViewModel = _serviceProvider.GetRequiredService<PluginDataViewModel>();

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

        _tutorialService.TutorialCompleted.Subscribe(_ => OnTutorialCompleted());

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
        
        await InitializeTrayIconAsync();
        
        // Check if tutorial should be shown, but only if conditions are met
        await CheckFirstRunAsync();

        _logger.Debug("InitializeAsync completed");
    }

    private async Task CheckFirstRunAsync()
    {
        try
        {
            _logger.Debug("Checking for first run...");
            
            if (_hasShownFirstRunTutorial)
            {
                _logger.Debug("Tutorial already shown this session, skipping");
                return;
            }

            // Check if user has chosen Sentry option
            var userHasChosenSentry = (bool)_configurationService.ReturnConfigValue(c => c.Common.UserChoseSentry);
            if (!userHasChosenSentry)
            {
                _logger.Debug("User has not chosen Sentry option yet, deferring tutorial");
                return;
            }

            // Check if update overlay is visible
            if (UpdatePromptViewModel.IsVisible)
            {
                _logger.Debug("Update overlay is visible, deferring tutorial");
                return;
            }

            var shouldShowTutorial = ShouldShowFirstRunTutorial();
            
            _logger.Debug("First run check - ShouldShowTutorial: {ShouldShow}", shouldShowTutorial);
            
            if (shouldShowTutorial)
            {
                await StartTutorialAfterDelay();
            }
            else
            {
                _logger.Debug("Not first run, skipping tutorial");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during first run check");
        }
    }

    private async void OnSentryChoiceMade()
    {
        _logger.Debug("Sentry choice made, checking if tutorial should be shown");
        
        try
        {
            // Add a small delay to ensure the Sentry prompt is fully closed and state is updated
            await Task.Delay(1000);
            
            // Now check if tutorial should be shown
            await CheckFirstRunAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Sentry choice completion");
        }
    }

    private async Task StartTutorialAfterDelay()
    {
        try
        {
            _logger.Info("Starting first run tutorial after conditions are met");
            _hasShownFirstRunTutorial = true;
            
            await Task.Delay(500);
        
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StartFirstRunTutorial();
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error starting tutorial after conditions are met");
        }
    }
    
    private bool ShouldShowFirstRunTutorial()
    {
        try
        {
            var downloadPath = string.Empty;
            var firstRunComplete = false;
            
            try
            {
                var downloadPathValue = _configurationService.ReturnConfigValue(c => c.BackgroundWorker.DownloadPath);
                var firstRunCompleteValue = _configurationService.ReturnConfigValue(c => c.Common.FirstRunComplete);
                
                downloadPath = downloadPathValue?.ToString() ?? string.Empty;
                firstRunComplete = firstRunCompleteValue is bool value && value;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Configuration properties not found, assuming first run");
                return true;
            }
            
            if (!firstRunComplete || string.IsNullOrEmpty(downloadPath))
            {
                _logger.Debug("First run not complete or download path empty - FirstRunComplete: {FirstRunComplete}, DownloadPath: '{DownloadPath}'", 
                    firstRunComplete, downloadPath);
                return true;
            }

            _logger.Debug("First run already completed and download path is set - FirstRunComplete: {FirstRunComplete}, DownloadPath: '{DownloadPath}'", 
                firstRunComplete, downloadPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error checking if should show tutorial, defaulting to show");
            return true;
        }
    }

    private void OnTutorialCompleted()
    {
        _logger.Info("Tutorial completed");
        
        try
        {
            var configModel = _configurationService.GetConfiguration();
            if (configModel?.Common != null)
            {
                if (_isFirstRunTutorial)
                {
                    configModel.Common.FirstRunComplete = true;
                    _configurationService.SaveConfiguration(configModel);
                    _logger.Info("First run tutorial completed and saved to configuration");
                    
                    // Start plugin tutorial after first run
                    _ = Task.Run(async () =>
                    {
                        await StartPluginTutorialAfterFirstRun();
                    });
                }
                else
                {
                    _logger.Info("Feature tutorial completed");
                }
            }
            else
            {
                _logger.Warn("Could not access configuration model to save first run completion");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save first run completion status");
        }
    }
    
    private void StartFirstRunTutorial()
    {
        _logger.Info("Starting first run tutorial");
    
        _isFirstRunTutorial = true;
        var steps = FirstRunTutorialSteps.GetFirstRunSteps(_configurationService);
        _tutorialService.StartTutorial(steps, isFirstRun: true);
    }
    
    public void StartFeatureTutorial(string featureName)
    {
        _logger.Info("Starting feature tutorial for: {FeatureName}", featureName);
    
        _isFirstRunTutorial = false;
        var steps = FirstRunTutorialSteps.GetFeatureTutorialSteps(featureName, _configurationService);
    
        if (steps.Count > 0)
        {
            _tutorialService.StartTutorial(steps, isFirstRun: false);
        }
        else
        {
            _logger.Warn("No tutorial steps found for feature: {FeatureName}", featureName);
        }
    }
    
    public void StartPluginsTutorial()
    {
        StartFeatureTutorial("plugins");
    }

    private async Task StartPluginTutorialAfterFirstRun()
    {
        try
        {
            _logger.Info("Starting plugin tutorial after first run completion");
            
            await Task.Delay(2000);
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Navigate to plugins page first
                SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Label == "Plugins");
                
                // Start plugin tutorial
                StartFeatureTutorial("plugins");
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error starting plugin tutorial after first run");
        }
    }
    
    private async Task OnTutorialServiceNavigationRequested(string destination)
    {
        _logger.Debug("Tutorial service requested navigation to: {Destination}", destination);
    
        try
        {
            await Task.Run(() =>
            {
                switch (destination.ToLower())
                {
                    case "home":
                        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Label == "Home");
                        break;
                    case "mods":
                        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Label == "Mods");
                        break;
                    case "plugins":
                        SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Label == "Plugins");
                        break;
                    case "settings":
                        NavigateToSettingsCommand.Execute(null);
                        break;
                    default:
                        _logger.Warn("Unknown tutorial navigation destination: {Destination}", destination);
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling tutorial navigation to: {Destination}", destination);
        }
    }

    private void OnTutorialTabNavigationRequested(string elementName)
    {
        _logger.Debug("Tutorial service requested tab navigation to element: {ElementName}", elementName);
    
        if (CurrentPage is SettingsViewModel settingsViewModel)
        {
            settingsViewModel.NavigateToElement(elementName);
        }
    }
    
    private void OnTutorialNavigationRequested(string destination)
    {
        _logger.Debug("Tutorial requested navigation to: {Destination}", destination);
    
        try
        {
            switch (destination.ToLower())
            {
                case "home":
                    SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Label == "Home");
                    break;
                case "mods":
                    SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Label == "Mods");
                    break;
                case "plugins":
                    SelectedMenuItem = MenuItems.FirstOrDefault(m => m.Label == "Plugins");
                    break;
                case "settings":
                    NavigateToSettingsCommand.Execute(null);
                    break;
                default:
                    _logger.Warn("Unknown tutorial navigation destination: {Destination}", destination);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling tutorial navigation to: {Destination}", destination);
        }
    }

    private void SetupConfigurationListener()
    {
        _configurationListener.TutorialRelevantConfigurationChanged += (sender, e) =>
        {
            _logger.Debug("Tutorial-relevant configuration changed: {PropertyName}", e.PropertyName);
            
            TutorialViewModel.RefreshCanProceed();
        };
    }

    
    private async Task InitializeTrayIconAsync()
    {
        try
        {
            _logger.Debug("Initializing tray icon");
            
            await Task.Delay(250);
            
            _trayIconManager.InitializeTrayIcon();
            _logger.Info("Tray icon initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize tray icon");
        }
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
            _logger.Debug("CheckForUpdatesAsync started. UpdateCheckService.IsUpdateAvailable: {IsAvailable}", _updateCheckService.IsUpdateAvailable);
        
            if (!_updateCheckService.IsUpdateAvailable)
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsCheckingForUpdates = true);
                
                _logger.Debug("Checking for updates using UpdateCheckService...");
            
                var hasUpdate = await _updateCheckService.CheckForUpdatesAsync();
            
                _logger.Debug("Update check call completed. HasUpdate: {HasUpdate}", hasUpdate);
                
                if (hasUpdate)
                {
                    _logger.Debug("Update detected, triggering UpdatePromptViewModel.CheckForUpdatesAsync");
                    await UpdatePromptViewModel.CheckForUpdatesAsync(_currentVersion);
                }
            }
            else
            {
                _logger.Debug("Skipping update check - update is already available");
                if (!UpdatePromptViewModel.IsVisible)
                {
                    _logger.Debug("Update is available but prompt not visible, triggering UpdatePromptViewModel.CheckForUpdatesAsync");
                    await UpdatePromptViewModel.CheckForUpdatesAsync(_currentVersion);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for updates");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsCheckingForUpdates = false);
        }
    }

    private void OnPluginSettingsRequested(PluginSettingsViewModel settingsViewModel)
    {
        _logger.Info("Plugin settings requested for {PluginId}", settingsViewModel.Plugin.PluginId);
    
        settingsViewModel.Closed += () => {
            _logger.Info("Plugin settings dialog closed, clearing PluginSettingsViewModel");
            PluginSettingsViewModel = null;
        };
    
        settingsViewModel.Show();
        PluginSettingsViewModel = settingsViewModel;
    }
    
    public void Dispose()
    {
        if (TutorialViewModel != null)
        {
            TutorialViewModel.NavigationRequested -= OnTutorialNavigationRequested;
        }
        
        if (_tutorialService != null)
        {
            _tutorialService.NavigationRequested -= OnTutorialServiceNavigationRequested;
            _tutorialService.TabNavigationRequested -= OnTutorialTabNavigationRequested;
        }
        
        _sentryAcceptSubscription?.Dispose();
        _sentryDeclineSubscription?.Dispose();
        
        _updateCheckTimer?.Stop();
        _updateCheckTimer?.Dispose();
        SentryPromptViewModel?.Dispose();
    }
}