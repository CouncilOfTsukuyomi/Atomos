using Atomos.BackgroundWorker.Extensions;
using Atomos.BackgroundWorker.Interfaces;
using CommonLib.Events;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.BackgroundWorker.Services;

public class ConfigurationListener : IConfigurationListener, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private bool _disposed;

    public ConfigurationListener(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
            
        StartListening();
    }

    private void StartListening()
    {
        _logger.Debug("Configuration Listen Events hooked");
        _configurationService.ConfigurationChanged += ConfigurationServiceOnConfigurationChanged;
    }

    private void ConfigurationServiceOnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        if (_disposed) return;
        
        _logger.Debug($"Detected change in {e.PropertyName}");

        switch (e.PropertyName)
        {
            case "Common.EnableSentry" when e.NewValue is bool shouldEnableSentry:
                HandleSentryChange(shouldEnableSentry);
                break;

            case "AdvancedOptions.EnableDebugLogs" when e.NewValue is bool shouldEnableLogging:
                HandleDebugLogsChange(shouldEnableLogging);
                break;
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