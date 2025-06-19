using System.Runtime.InteropServices;
using CommonLib.Events;
using CommonLib.Interfaces;
using NLog;
using PenumbraModForwarder.UI.Extensions;
using PenumbraModForwarder.UI.Interfaces;

namespace PenumbraModForwarder.UI.Services;

public class ConfigurationListener : IConfigurationListener
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly IXivLauncherService _xivLauncherService;
    private readonly IFileLinkingService _fileLinkingService;

    public ConfigurationListener(
        IConfigurationService configurationService,
        IXivLauncherService xivLauncherService,
        IFileLinkingService fileLinkingService)
    {
        _configurationService = configurationService;
        _xivLauncherService = xivLauncherService;
        _fileLinkingService = fileLinkingService;

        StartListening();
        InitializeConfigurationState();
    }

    /// <summary>
    /// Hook the ConfigurationService event so that future changes are picked up.
    /// </summary>
    private void StartListening()
    {
        _logger.Debug("Configuration Listen Events hooked");
        _configurationService.ConfigurationChanged += ConfigurationServiceOnConfigurationChanged;
    }

    /// <summary>
    /// Run the same logic you would in the events, but do it once on startup.
    /// This ensures you pick up any changes that happened before the UI ran.
    /// </summary>
    private void InitializeConfigurationState()
    {
        var config = _configurationService.GetConfiguration();
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (config.Common.FileLinkingEnabled)
            {
                _logger.Debug("File linking enabled in initilization");
                _fileLinkingService.EnableFileLinking();
            }

            if (config.Common.StartOnBoot)
            {
                _logger.Debug("Start on boot enabled in initilization");
                _fileLinkingService.EnableStartup();
            }
        }
    }

    /// <summary>
    /// Event handler that fires whenever the configuration changes at runtime.
    /// </summary>
    private void ConfigurationServiceOnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        _logger.Debug($"Detected change in {e.PropertyName}");

        if (e is { PropertyName: "Common.StartOnFfxivBoot", NewValue: bool shouldAutoStart })
        {
            _xivLauncherService.EnableAutoStartWatchdog(shouldAutoStart);
        }

        // Only apply changes that matter on Windows
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (e is { PropertyName: "Common.FileLinkingEnabled", NewValue: bool shouldLinkFiles })
            {
                if (shouldLinkFiles)
                {
                    _fileLinkingService.EnableFileLinking();
                }
                else
                {
                    _fileLinkingService.DisableFileLinking();
                }
            }

            if (e is { PropertyName: "Common.StartOnBoot", NewValue: bool shouldStartOnBoot })
            {
                if (shouldStartOnBoot)
                {
                    _fileLinkingService.EnableStartup();
                }
                else
                {
                    _fileLinkingService.DisableStartup();
                }
            }
        }


        if (e is { PropertyName: "Common.EnableSentry", NewValue: bool shouldEnableSentry })
        {
            if (shouldEnableSentry)
            {
                _logger.Debug("EnableSentry event triggered");
                DependencyInjection.EnableSentryLogging();
            }
            else
            {
                _logger.Debug("DisableSentry event triggered");
                DependencyInjection.DisableSentryLogging();
            }
        }
    }
}