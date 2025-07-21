using NLog;
using Atomos.BackgroundWorker.Extensions;
using Atomos.BackgroundWorker.Helpers;
using Atomos.BackgroundWorker.Interfaces;

public class Program
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static Mutex _appMutex;

    public static void Main(string[] args)
    {
        _logger.Info("=== Atomos Background Worker Starting ===");
        _logger.Info("Process ID: {ProcessId}", Environment.ProcessId);
        _logger.Info("Command line arguments: [{Args}]", string.Join(", ", args));
        _logger.Info("Current directory: {WorkingDirectory}", Environment.CurrentDirectory);
        _logger.Info("Base directory: {BaseDirectory}", AppContext.BaseDirectory);
        
        LogSystemDiagnostics();

        _logger.Info("Attempting to acquire application mutex...");
        bool isNewInstance;
        _appMutex = new Mutex(true, "Atomos.BackgroundWorker", out isNewInstance);
        
        if (!isNewInstance)
        {
            _logger.Warn("Another instance of Background Worker is already running - this is expected during rapid restart cycles");
            _logger.Info("Exiting gracefully to allow primary instance to continue");
            _appMutex?.Dispose();
            return;
        }
        
        _logger.Info("Mutex acquired successfully - this is the primary Background Worker instance");

        try
        {
            _logger.Info("Creating application host builder...");
            var builder = Host.CreateApplicationBuilder(args);
            _logger.Info("Application host builder created successfully");

            bool isInitializedByWatchdog = Environment.GetEnvironmentVariable("WATCHDOG_INITIALIZED") == "true";
            _logger.Info("Environment check - Watchdog initialization flag: {IsInitialized}", isInitializedByWatchdog);
            
            LogEnvironmentVariables();

#if DEBUG
            _logger.Info("DEBUG MODE: Overriding watchdog initialization check");
            isInitializedByWatchdog = true;
            if (args.Length == 0)
            {
                args = new string[] { "12345" };
                _logger.Info("DEBUG MODE: Using default port 12345 since no arguments provided");
            }
#endif

            if (!isInitializedByWatchdog)
            {
                _logger.Error("STARTUP FAILED: Background Worker was not started by the Atomos Watchdog");
                _logger.Error("This application must be launched through Atomos.Watchdog.exe");
                _logger.Error("Direct execution is blocked for security and stability reasons");
                return;
            }

            _logger.Info("Validating startup arguments...");
            if (args.Length == 0)
            {
                _logger.Fatal("STARTUP FAILED: No communication port specified by launcher");
                _logger.Fatal("This indicates a critical problem with the watchdog startup sequence");
                _logger.Fatal("Expected: Atomos.BackgroundWorker.exe <port_number>");
                return;
            }

            if (!int.TryParse(args[0], out var port))
            {
                _logger.Fatal("STARTUP FAILED: Invalid port argument '{PortArg}'", args[0]);
                _logger.Fatal("Expected numeric port value between 1024-65535");
                return;
            }

            if (port < 1024 || port > 65535)
            {
                _logger.Fatal("STARTUP FAILED: Port {Port} is outside valid range (1024-65535)", port);
                return;
            }

            _logger.Info("Startup validation passed - using communication port {Port}", port);

            _logger.Info("Registering application services...");
            try
            {
                builder.Services.AddApplicationServices(port);
                _logger.Info("Application services registered successfully");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "STARTUP FAILED: Error during service registration");
                _logger.Fatal("This usually indicates missing dependencies or configuration issues");
                throw;
            }

            _logger.Info("Building application host...");
            var host = builder.Build();
            _logger.Info("Application host built successfully");
            
            _logger.Info("Setting up logging and WebSocket integration...");
            try
            {
                var nlogConfig = LogManager.Configuration;
                var webSocketServer = host.Services.GetRequiredService<IWebSocketServer>();
                _logger.Info("WebSocket server instance obtained from DI container");
                
                nlogConfig.AddWebHookTarget(
                    targetName: "webHookTarget",
                    webSocketServer: webSocketServer,
                    minLevel: NLog.LogLevel.Error,
                    endpoint: "/error"
                );
                LogManager.Configuration = nlogConfig;
                _logger.Info("Error logging webhook configured for real-time error reporting");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Warning: Could not configure WebSocket error reporting - continuing without it");
            }
            
            _logger.Info("Configuring graceful shutdown handlers...");
            Console.CancelKeyPress += (sender, e) =>
            {
                _logger.Info("Received shutdown signal (Ctrl+C) - initiating graceful shutdown...");
                e.Cancel = true;
                
                var shutdownTask = host.StopAsync(TimeSpan.FromSeconds(10));
                if (!shutdownTask.Wait(TimeSpan.FromSeconds(15)))
                {
                    _logger.Warn("Graceful shutdown timed out - forcing exit");
                    Environment.Exit(1);
                }
                _logger.Info("Graceful shutdown completed");
            };
            
            _logger.Info("Shutdown handlers configured");
            _logger.Info("=== Background Worker Startup Complete ===");
            _logger.Info("Starting main application host - Background Worker is now running");
            
            host.Run();
            
            _logger.Info("Application host has stopped normally");
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "CRITICAL ERROR during Background Worker startup");
            _logger.Fatal("Exception Type: {ExceptionType}", ex.GetType().Name);
            _logger.Fatal("Exception Message: {Message}", ex.Message);
            if (ex.InnerException != null)
            {
                _logger.Fatal("Inner Exception: {InnerException}", ex.InnerException.Message);
            }
            _logger.Fatal("Stack Trace: {StackTrace}", ex.StackTrace);
            Environment.Exit(1);
        }
        finally
        {
            _logger.Info("=== Background Worker Shutdown Sequence ===");
            _logger.Info("Releasing application mutex...");
            try
            {
                _appMutex?.ReleaseMutex();
                _logger.Info("Application mutex released successfully");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Warning: Error releasing mutex (this is usually harmless)");
            }
            
            try
            {
                _appMutex?.Dispose();
                _logger.Info("Mutex disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Warning: Error disposing mutex");
            }
            
            _logger.Info("=== Background Worker Shutdown Complete ===");
        }
    }

    private static void LogSystemDiagnostics()
    {
        _logger.Info("=== System Diagnostics ===");
        _logger.Info("Operating System: {OS}", Environment.OSVersion);
        _logger.Info("Runtime: {Runtime}", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        _logger.Info("Process Architecture: {Architecture}", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        _logger.Info("OS Architecture: {OSArchitecture}", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        _logger.Info("========================");
    }

    private static void LogEnvironmentVariables()
    {
        _logger.Info("=== Relevant Environment Variables ===");
        var relevantVars = new[] 
        { 
            "WATCHDOG_INITIALIZED", 
            "WATCHDOG_SHOW_CONSOLE", 
            "DEV_MODE",
            "DOTNET_ENVIRONMENT",
            "ASPNETCORE_ENVIRONMENT"
        };

        foreach (var varName in relevantVars)
        {
            var value = Environment.GetEnvironmentVariable(varName);
            _logger.Info("{VarName}: {Value}", varName, value ?? "<not set>");
        }
        _logger.Info("======================================");
    }
}