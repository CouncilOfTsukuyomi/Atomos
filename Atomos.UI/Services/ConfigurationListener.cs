using System;
using System.Runtime.InteropServices;
using Atomos.UI.Extensions;
using Atomos.UI.Interfaces;
using CommonLib.Events;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.UI.Services;

public class ConfigurationListener : IConfigurationListener, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly IXivLauncherService _xivLauncherService;
    private readonly IFileLinkingService _fileLinkingService;
    private bool _disposed;

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
                _logger.Debug("File linking enabled in initialization");
                _fileLinkingService.EnableFileLinking();
            }

            if (config.Common.StartOnBoot)
            {
                _logger.Debug("Start on boot enabled in initialization");
                _fileLinkingService.EnableStartup();
            }
        }
    }

    /// <summary>
    /// Event handler that fires whenever the configuration changes at runtime.
    /// </summary>
    private void ConfigurationServiceOnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        if (_disposed) return;
        
        _logger.Debug($"Detected change in {e.PropertyName}");

        switch (e.PropertyName)
        {
            case "Common.StartOnFfxivBoot" when e.NewValue is bool shouldAutoStart:
                _xivLauncherService.EnableAutoStartWatchdog(shouldAutoStart);
                break;

            case "Common.FileLinkingEnabled" when e.NewValue is bool shouldLinkFiles:
                HandleFileLinkingChange(shouldLinkFiles);
                break;

            case "Common.StartOnBoot" when e.NewValue is bool shouldStartOnBoot:
                HandleStartOnBootChange(shouldStartOnBoot);
                break;

            case "Common.EnableSentry" when e.NewValue is bool shouldEnableSentry:
                HandleSentryChange(shouldEnableSentry);
                break;

            case "Common.EnableDebugLogs" when e.NewValue is bool shouldEnableLogging:
                HandleDebugLogsChange(shouldEnableLogging);
                break;
        }
    }

    private void HandleFileLinkingChange(bool shouldLinkFiles)
    {
        // Only apply changes that matter on Windows
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        if (shouldLinkFiles)
        {
            _fileLinkingService.EnableFileLinking();
        }
        else
        {
            _fileLinkingService.DisableFileLinking();
        }
    }

    private void HandleStartOnBootChange(bool shouldStartOnBoot)
    {
        // Only apply changes that matter on Windows
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        if (shouldStartOnBoot)
        {
            _fileLinkingService.EnableStartup();
        }
        else
        {
            _fileLinkingService.DisableStartup();
        }
    }

    private void HandleSentryChange(bool shouldEnableSentry)
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

    private void HandleDebugLogsChange(bool shouldEnableLogging)
    {
        if (shouldEnableLogging)
        {
            _logger.Debug("Enabling debug logs");
            DependencyInjection.EnableDebugLogging();
        }
        else
        {
            _logger.Debug("Disabling debug logs");
            DependencyInjection.DisableDebugLogging();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _configurationService.ConfigurationChanged -= ConfigurationServiceOnConfigurationChanged;
        _disposed = true;
    }
}