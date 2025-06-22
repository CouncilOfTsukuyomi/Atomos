using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Atomos.Watchdog.Imports;
using Atomos.Watchdog.Interfaces;
using CommonLib.Interfaces;
using CommonLib.Services;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Atomos.Watchdog.Extensions;

namespace Atomos.Watchdog;

internal class Program
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly IProcessManager _processManager;
    private readonly IConfigurationSetup _configurationSetup;
    private readonly IUpdateService _updateService;
    private CancellationTokenSource? _cts;

    public Program(
        IConfigurationService configurationService,
        IProcessManager processManager,
        IConfigurationSetup configurationSetup,
        IUpdateService updateService)
    {
        _configurationService = configurationService;
        _processManager = processManager;
        _configurationSetup = configurationSetup;
        _updateService = updateService;
        _cts = new CancellationTokenSource();
    }

    private static void Main(string[] args)
    {
        using (new Mutex(true, "Atomos.Launcher", out var isNewInstance))
        {
            if (!isNewInstance)
            {
                Console.WriteLine("Another instance is already running. Exiting...");
                return;
            }

            var services = new ServiceCollection();
            services.AddApplicationServices();
            services.AddSingleton<Program>();

            var serviceProvider = services.BuildServiceProvider();
            var program = serviceProvider.GetRequiredService<Program>();
            program.Run();
        }
    }

    private void Run()
    {
        try
        {
            _configurationSetup.CreateFiles();

            // Inform that the Watchdog is up
            ApplicationBootstrapper.SetWatchdogInitialization();
            Environment.SetEnvironmentVariable("WATCHDOG_INITIALIZED", "true");

            // Hide console if on Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                HideConsoleWindow();
            }

            // Main logic
            _processManager.Run();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Main Run() loop encountered an error.");
        }
        finally
        {
            // Attempt normal cleanup
            try
            {
                CancelRunningTasks();
                
                // Properly dispose of ProcessManager to ensure child processes are cleaned up
                _logger.Info("Disposing ProcessManager...");
                _processManager?.Dispose();
                
                LogActiveThreads("Final Cleanup Before Exit");
                
                // Give a moment for cleanup to complete
                Thread.Sleep(1000);
                
                LogManager.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Cleanup phase encountered an error.");
            }

            _logger.Info("Exiting application normally...");
        }
    }

    private void HideConsoleWindow()
    {
        var showWindow = (bool)_configurationService.ReturnConfigValue(
            config => config.AdvancedOptions.ShowWatchDogWindow
        );

        if (showWindow)
        {
            _logger.Info("Watchdog window remains visible per configuration.");
            return;
        }

        var handle = DllImports.GetConsoleWindow();
        if (handle == IntPtr.Zero) return;

        _logger.Info("Hiding console window...");
        DllImports.ShowWindow(handle, DllImports.SW_HIDE);
    }

    private void CancelRunningTasks()
    {
        if (_cts == null || _cts.IsCancellationRequested)
            return;

        _logger.Info("Canceling any remaining tasks or threads...");
        _cts.Cancel();
    }

    private void LogActiveThreads(string context)
    {
        _logger.Info("=== Listing active threads: {0} ===", context);
        try
        {
            foreach (ProcessThread t in Process.GetCurrentProcess().Threads)
            {
                _logger.Info(" - Thread ID: {0}, State: {1}, Priority: {2}",
                    t.Id, t.ThreadState, t.PriorityLevel);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not list active threads.");
        }
    }
}