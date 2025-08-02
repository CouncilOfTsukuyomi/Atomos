using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.Interfaces.Tutorial;
using Atomos.UI.Models;
using Atomos.UI.Services.Tutorial;
using Atomos.UI.ViewModels;
using Avalonia.Threading;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.UI.Services;

public class TutorialManager : ITutorialManager
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
    private readonly ITutorialService _tutorialService;
    private readonly IElementHighlightService _elementHighlightService;
    private readonly IConfigurationService _configurationService;
        
    private bool _hasShownFirstRunTutorial = false;
    private bool _isFirstRunTutorial = false;
        
    // These will be set by the MainWindowViewModel
    private Func<ObservableCollection<MenuItem>>? _getMenuItems;
    private Action<MenuItem>? _setSelectedMenuItem;
    private Action<ViewModelBase>? _setCurrentPage;
    private Func<ViewModelBase>? _navigateToSettings;
        
    public TutorialOverlayViewModel TutorialViewModel { get; }

    public event Action<string>? NavigationRequested;

    public TutorialManager(
        ITutorialService tutorialService,
        IElementHighlightService elementHighlightService,
        IConfigurationService configurationService)
    {
        _tutorialService = tutorialService;
        _elementHighlightService = elementHighlightService;
        _configurationService = configurationService;

        TutorialViewModel = new TutorialOverlayViewModel(tutorialService, elementHighlightService);
        TutorialViewModel.NavigationRequested += OnTutorialNavigationRequested;
        tutorialService.NavigationRequested += OnTutorialServiceNavigationRequested;
        tutorialService.TabNavigationRequested += OnTutorialTabNavigationRequested;
        tutorialService.TutorialCompleted.Subscribe(_ => OnTutorialCompleted());
    }

    public void Initialize(
        Func<ObservableCollection<MenuItem>> getMenuItems,
        Action<MenuItem> setSelectedMenuItem,
        Action<ViewModelBase> setCurrentPage,
        Func<ViewModelBase> navigateToSettings)
    {
        _getMenuItems = getMenuItems;
        _setSelectedMenuItem = setSelectedMenuItem;
        _setCurrentPage = setCurrentPage;
        _navigateToSettings = navigateToSettings;
    }

    public async Task CheckFirstRunAsync()
    {
        try
        {
            _logger.Debug("Checking for first run...");
                
            if (_hasShownFirstRunTutorial)
            {
                _logger.Debug("Tutorial already shown this session, skipping");
                return;
            }

            var userHasChosenSentry = (bool)_configurationService.ReturnConfigValue(c => c.Common.UserChoseSentry);
            if (!userHasChosenSentry)
            {
                _logger.Debug("User has not chosen Sentry option yet, deferring tutorial");
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

    public void StartFirstRunTutorial()
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
                    _configurationService.UpdateConfigValue(
                        config => config.Common.FirstRunComplete = true,
                        "Common.FirstRunComplete",
                        true);
                    _logger.Info("First run tutorial completed and saved to configuration");
                        
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

    private async Task StartPluginTutorialAfterFirstRun()
    {
        try
        {
            _logger.Info("Starting plugin tutorial after first run completion");
                
            await Task.Delay(2000);
                
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_getMenuItems != null && _setSelectedMenuItem != null)
                {
                    var pluginMenuItem = _getMenuItems().FirstOrDefault(m => m.Label == "Plugins");
                    if (pluginMenuItem != null)
                    {
                        _setSelectedMenuItem(pluginMenuItem);
                    }
                }
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
                HandleNavigation(destination);
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
        NavigationRequested?.Invoke(elementName);
    }
        
    private void OnTutorialNavigationRequested(string destination)
    {
        _logger.Debug("Tutorial requested navigation to: {Destination}", destination);
        HandleNavigation(destination);
    }

    private void HandleNavigation(string destination)
    {
        if (_getMenuItems == null || _setSelectedMenuItem == null || _setCurrentPage == null || _navigateToSettings == null)
        {
            _logger.Warn("Navigation delegates not initialized");
            return;
        }

        try
        {
            switch (destination.ToLower())
            {
                case "home":
                    var homeMenuItem = _getMenuItems().FirstOrDefault(m => m.Label == "Home");
                    if (homeMenuItem != null) _setSelectedMenuItem(homeMenuItem);
                    break;
                case "mods":
                    var modsMenuItem = _getMenuItems().FirstOrDefault(m => m.Label == "Mods");
                    if (modsMenuItem != null) _setSelectedMenuItem(modsMenuItem);
                    break;
                case "plugins":
                    var pluginsMenuItem = _getMenuItems().FirstOrDefault(m => m.Label == "Plugins");
                    if (pluginsMenuItem != null) _setSelectedMenuItem(pluginsMenuItem);
                    break;
                case "settings":
                    var settingsPage = _navigateToSettings();
                    _setCurrentPage(settingsPage);
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
    }
}