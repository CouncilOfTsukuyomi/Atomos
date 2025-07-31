using Atomos.BackgroundWorker.Interfaces;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.BackgroundWorker;

public class Worker : BackgroundService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IWebSocketServer _webSocketServer;
    private readonly IStartupService _startupService;
    private readonly IConfigurationService _configurationService;
    private readonly int _port;
    private readonly IHostApplicationLifetime _lifetime;
    private bool _initialized;

    public Worker(
        IWebSocketServer webSocketServer,
        IStartupService startupService,
        IConfigurationService configurationService,
        int port,
        IHostApplicationLifetime lifetime)
    {
        _webSocketServer = webSocketServer;
        _startupService = startupService;
        _configurationService = configurationService;
        _port = port;
        _lifetime = lifetime;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Starting WebSocket Server on port {Port}", _port);
        _webSocketServer.Start(_port);

        _logger.Info("Launching file watcher...");
        
        _ = Task.Run(() => ListenForShutdownCommand(cancellationToken), cancellationToken);
        
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_initialized && _webSocketServer.HasConnectedClients())
                {
                    await _startupService.InitializeAsync();
                    _initialized = true;
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Worker stopping gracefully...");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error occurred in worker");
            throw;
        }
        finally
        {
            try
            {
                _logger.Info("Flushing configuration changes before shutdown...");
                _configurationService?.FlushPendingChangesSync();
                _logger.Info("Configuration flush completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error flushing configuration during shutdown");
            }
        }
    }

    private async Task ListenForShutdownCommand(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (Console.In.Peek() > -1)
            {
                var line = await Console.In.ReadLineAsync();
                if (line != null && line.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info("Received 'shutdown' command via standard input, stopping application...");
                    
                    try
                    {
                        _logger.Info("Flushing configuration changes before shutdown command...");
                        _configurationService?.FlushPendingChangesSync();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error flushing configuration during shutdown command");
                    }
                    
                    _lifetime.StopApplication();
                    break;
                }
            }
            
            await Task.Delay(500, stoppingToken);
        }
    }
}