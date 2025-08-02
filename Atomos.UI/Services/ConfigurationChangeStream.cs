using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Atomos.UI.Interfaces;
using CommonLib.Events;
using CommonLib.Interfaces;

namespace Atomos.UI.Services;

public class ConfigurationChangeStream : IConfigurationChangeStream, IDisposable
{
    private readonly Subject<ConfigurationChangedEventArgs> _configChanges = new();
    private readonly IConfigurationService _configurationService;
    private readonly IWebSocketClient _webSocketClient;

    public IObservable<ConfigurationChangedEventArgs> ConfigurationChanges => _configChanges.AsObservable();

    public ConfigurationChangeStream(IConfigurationService configurationService, IWebSocketClient webSocketClient)
    {
        _configurationService = configurationService;
        _webSocketClient = webSocketClient;
        
        _configurationService.ConfigurationChanged += OnLocalConfigChanged;
        
        if (_webSocketClient is WebSocketClient wsClient)
        {
            wsClient.ConfigurationChanged += OnWebSocketConfigChanged;
        }
    }

    private void OnLocalConfigChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        _configChanges.OnNext(e);
    }

    private void OnWebSocketConfigChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        _configChanges.OnNext(e);
    }

    public void Dispose()
    {
        _configurationService.ConfigurationChanged -= OnLocalConfigChanged;
        
        if (_webSocketClient is WebSocketClient wsClient)
        {
            wsClient.ConfigurationChanged -= OnWebSocketConfigChanged;
        }
        
        _configChanges?.Dispose();
    }
}