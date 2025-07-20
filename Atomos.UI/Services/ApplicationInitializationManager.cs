using System;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using CommonLib.Interfaces;
using NLog;
using Timer = System.Timers.Timer;

namespace Atomos.UI.Services;

public class ApplicationInitializationManager : IApplicationInitializationManager
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
    private readonly IWebSocketClient _webSocketClient;
    private readonly ITrayIconManager _trayIconManager;
    private readonly INotificationService _notificationService;
        
    private Timer? _updateCheckTimer;

    public ApplicationInitializationManager(
        IWebSocketClient webSocketClient,
        ITrayIconManager trayIconManager,
        INotificationService notificationService)
    {
        _webSocketClient = webSocketClient;
        _trayIconManager = trayIconManager;
        _notificationService = notificationService;
    }

    public async Task InitializeAsync(int port, Func<Task> checkForUpdatesAsync, Func<Task> checkFirstRunAsync)
    {
        _logger.Debug("Starting InitializeAsync with port: {Port}", port);
            
        StartUpdateCheckTimer(checkForUpdatesAsync);
            
        _logger.Debug("Starting initial update check");
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.Debug("About to call CheckForUpdatesAsync from Task.Run");
                await checkForUpdatesAsync();
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
            
        await checkFirstRunAsync();

        _logger.Debug("InitializeAsync completed");
    }

    private void StartUpdateCheckTimer(Func<Task> checkForUpdatesAsync)
    {
        _logger.Debug("Starting update check timer (5 minute intervals)");
        
        _updateCheckTimer = new Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
        _updateCheckTimer.Elapsed += async (sender, e) =>
        {
            try
            {
                _logger.Debug("Timer elapsed - starting scheduled update check");
                await checkForUpdatesAsync();
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

    public void Dispose()
    {
        _updateCheckTimer?.Stop();
        _updateCheckTimer?.Dispose();
    }
}