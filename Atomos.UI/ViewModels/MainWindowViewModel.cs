﻿using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Atomos.UI.Extensions;
using Atomos.UI.Interfaces;
using Atomos.UI.Models;
using Atomos.UI.Services;
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

namespace Atomos.UI.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationService _notificationService;
    private readonly ISoundManagerService _soundManagerService;
    private readonly IConfigurationListener _configurationListener;
    private readonly IConfigurationService _configurationService;
    private readonly ITaskbarFlashService _taskbarFlashService;
    private readonly ITutorialManager _tutorialManager;
    private readonly IApplicationInitializationManager _initializationManager;
    private readonly IUpdateManager _updateManager;
    private readonly ISentryManager _sentryManager;
        
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
        
    public bool IsCheckingForUpdates => _updateManager.IsCheckingForUpdates;
    public bool HasUpdateAvailable => _updateManager.HasUpdateAvailable;
    public string UpdateAvailableText => _updateManager.UpdateAvailableText;

    public bool ShowCustomTitleBar => !RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public ObservableCollection<MenuItem> MenuItems { get; private set; } = new();
    public ObservableCollection<Notification> Notifications =>
        (_notificationService as NotificationService)?.Notifications ?? new();

    public InstallViewModel InstallViewModel { get; private set; } = null!;
    public SentryPromptViewModel SentryPromptViewModel => _sentryManager.SentryPromptViewModel;
    public NotificationHubViewModel NotificationHubViewModel { get; private set; } = null!;
    public UpdatePromptViewModel UpdatePromptViewModel => _updateManager.UpdatePromptViewModel;
    public TutorialOverlayViewModel TutorialViewModel => _tutorialManager.TutorialViewModel;

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

    public ICommand NavigateToSettingsCommand { get; private set; } = null!;
    public ICommand NavigateToAboutCommand { get; private set; } = null!;
    public ICommand ShowUpdatePromptCommand { get; private set; } = null!;

    public MainWindowViewModel(
        IServiceProvider serviceProvider,
        INotificationService notificationService,
        int port,
        IConfigurationListener configurationListener,
        ISoundManagerService soundManagerService,
        IConfigurationService configurationService,
        ITaskbarFlashService taskbarFlashService,
        ITutorialManager tutorialManager,
        IApplicationInitializationManager initializationManager,
        IUpdateManager updateManager,
        ISentryManager sentryManager)
    {
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
        _configurationListener = configurationListener;
        _soundManagerService = soundManagerService;
        _configurationService = configurationService;
        _taskbarFlashService = taskbarFlashService;
        _tutorialManager = tutorialManager;
        _initializationManager = initializationManager;
        _updateManager = updateManager;
        _sentryManager = sentryManager;

        _logger.Info("Platform detected - Windows: {IsWindows}, Linux: {IsLinux}, macOS: {IsMacOS}, Custom title bar: {ShowCustomTitleBar}",
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            ShowCustomTitleBar);

        LoadAppLogo();
        SetupConfigurationListener();
        InitializeVersion();
        ConfigureLogging();
        SetupManagers(port);
        SetupViewModels();
        SetupMenuItems();
        SetupCommands();

        _selectedMenuItem = MenuItems[0];
        _currentPage = _selectedMenuItem.ViewModel;

        InstallViewModel = new InstallViewModel(_serviceProvider.GetRequiredService<IWebSocketClient>(), _soundManagerService, _taskbarFlashService);

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

    private void InitializeVersion()
    {
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
    }

    private void ConfigureLogging()
    {
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
    }

    private void SetupManagers(int port)
    {
        _tutorialManager.Initialize(
            () => MenuItems,
            menuItem => SelectedMenuItem = menuItem,
            page => CurrentPage = page,
            () => ActivatorUtilities.CreateInstance<SettingsViewModel>(_serviceProvider));

        _tutorialManager.NavigationRequested += OnTutorialTabNavigationRequested;
        
        // Update checking status events
        _updateManager.IsCheckingForUpdatesChanged += isChecking =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                this.RaisePropertyChanged(nameof(IsCheckingForUpdates));
            }
            else
            {
                Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(IsCheckingForUpdates)));
            }
        };

        // Update available status events
        _updateManager.HasUpdateAvailableChanged += hasUpdate =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                this.RaisePropertyChanged(nameof(HasUpdateAvailable));
                this.RaisePropertyChanged(nameof(UpdateAvailableText));
            }
            else
            {
                Dispatcher.UIThread.Post(() => 
                {
                    this.RaisePropertyChanged(nameof(HasUpdateAvailable));
                    this.RaisePropertyChanged(nameof(UpdateAvailableText));
                });
            }
        };

        // Handle user clicking the update indicator
        _updateManager.ShowUpdatePromptRequested += OnShowUpdatePromptRequested;
        
        _sentryManager.SentryChoiceMade += OnSentryChoiceMade;
    }

    private void SetupViewModels()
    {
        NotificationHubViewModel = new NotificationHubViewModel(_notificationService, _configurationService);
    }

    private void SetupMenuItems()
    {
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
    }

    private void SetupCommands()
    {
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

        // New command for showing the update prompt when user clicks the update indicator
        ShowUpdatePromptCommand = ReactiveCommand.Create(() => _updateManager.ShowUpdatePrompt());
    }

    private async Task InitializeAsync(int port)
    {
        _logger.Debug("Starting InitializeAsync with port: {Port}", port);
            
        await _initializationManager.InitializeAsync(
            port, 
            () => _updateManager.CheckForUpdatesAsync(_currentVersion),
            () => _tutorialManager.CheckFirstRunAsync());

        _logger.Debug("InitializeAsync completed");
    }

    private async Task OnSentryChoiceMade()
    {
        _logger.Debug("Sentry choice made, checking if tutorial should be shown");
            
        try
        {
            await Task.Delay(1000);
            await _tutorialManager.CheckFirstRunAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Sentry choice completion");
        }
    }

    private async void OnShowUpdatePromptRequested()
    {
        _logger.Debug("Update prompt requested by user click");
        
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await UpdatePromptViewModel.CheckForUpdatesAsync(_currentVersion, isForced: false);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error showing update prompt on user request");
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

    private void SetupConfigurationListener()
    {
        _configurationListener.TutorialRelevantConfigurationChanged += (sender, e) =>
        {
            _logger.Debug("Tutorial-relevant configuration changed: {PropertyName}", e.PropertyName);
                
            TutorialViewModel.RefreshCanProceed();
        };
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
        _tutorialManager?.Dispose();
        _initializationManager?.Dispose();
        _updateManager?.Dispose();
        _sentryManager?.Dispose();
    }
}