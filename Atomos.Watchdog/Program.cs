using System.Diagnostics;
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
    private static bool _consoleAllocated = false;

    private readonly IConfigurationService _configurationService;
    private readonly IProcessManager _processManager;
    private readonly IConfigurationSetup _configurationSetup;
    private readonly IUpdateService _updateService;
    private CancellationTokenSource? _cts;

    public Program(IConfigurationService configurationService, IProcessManager processManager, 
        IConfigurationSetup configurationSetup, IUpdateService updateService)
    {
        _configurationService = configurationService;
        _processManager = processManager;
        _configurationSetup = configurationSetup;
        _updateService = updateService;
        _cts = new CancellationTokenSource();
    }

    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Atomos.Launcher", out var isNewInstance);
        if (!isNewInstance)
        {
            if (EnsureConsoleAvailable())
            {
                Console.WriteLine("Another instance is already running. Exiting...");
                Thread.Sleep(2000);
            }
            return;
        }

        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddSingleton<Program>();

        var serviceProvider = services.BuildServiceProvider();
        var program = serviceProvider.GetRequiredService<Program>();
        program.Run();
    }

    private void Run()
    {
        try
        {
            _configurationSetup.CreateFiles();
            ApplicationBootstrapper.SetWatchdogInitialization();
            Environment.SetEnvironmentVariable("WATCHDOG_INITIALIZED", "true");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ManageConsoleVisibility();

            _processManager.Run();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Main Run() loop encountered an error.");
            ShowErrorToUser(ex);
        }
        finally
        {
            PerformCleanup();
        }
    }

    private void ManageConsoleVisibility()
    {
        var showWindow = (bool)_configurationService.ReturnConfigValue(config => config.AdvancedOptions.ShowWatchDogWindow);

        if (showWindow)
        {
            _logger.Info("Watchdog window should remain visible per configuration.");
            if (EnsureConsoleAvailable())
            {
                var handle = DllImports.GetConsoleWindow();
                if (handle != IntPtr.Zero)
                {
                    DllImports.ShowWindow(handle, DllImports.SW_SHOW);
                    _logger.Info("Console window is now visible.");
                    Environment.SetEnvironmentVariable("WATCHDOG_SHOW_CONSOLE", "true");
                    DisplayStartupMessage();
                }
            }
            return;
        }

        _logger.Info("Hiding console window per configuration...");
        Environment.SetEnvironmentVariable("WATCHDOG_SHOW_CONSOLE", "false");
        HideConsoleWindow();
    }

    private void DisplayStartupMessage()
    {
        Console.WriteLine("=== Atomos Watchdog Starting ===");
        Console.WriteLine("Configuration allows window to remain visible.");
        Console.WriteLine("All log messages will be displayed here.");
        Console.WriteLine("=====================================");
    }

    private void HideConsoleWindow()
    {
        if (DllImports.FreeConsole())
        {
            _logger.Info("Console freed successfully.");
            _consoleAllocated = false;
            return;
        }

        var handle = DllImports.GetConsoleWindow();
        if (handle != IntPtr.Zero)
        {
            _logger.Info("Using traditional window hiding...");
            DllImports.ShowWindow(handle, DllImports.SW_HIDE);
            DllImports.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 
                DllImports.SWP_HIDEWINDOW | DllImports.SWP_NOACTIVATE | 
                DllImports.SWP_NOMOVE | DllImports.SWP_NOSIZE | DllImports.SWP_NOZORDER);
        }
        else
        {
            _logger.Info("No console window found to hide.");
        }
    }

    private static bool EnsureConsoleAvailable()
    {
        if (_consoleAllocated) return true;

        if (DllImports.AttachConsole(DllImports.ATTACH_PARENT_PROCESS))
        {
            _consoleAllocated = true;
            return true;
        }

        if (DllImports.AllocConsole())
        {
            _consoleAllocated = true;
            RedirectConsoleStreams();
            return true;
        }

        return false;
    }

    private static void RedirectConsoleStreams()
    {
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput()));
        }
        catch
        {
            // Ignore errors in stream redirection
        }
    }

    private void ShowErrorToUser(Exception ex)
    {
        if (EnsureConsoleAvailable())
        {
            Console.WriteLine($"Critical error occurred: {ex.Message}");
            Console.WriteLine("Check the log files for more details.");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    private void PerformCleanup()
    {
        try
        {
            _cts?.Cancel();
            _logger.Info("Disposing ProcessManager...");
            _processManager?.Dispose();
            LogActiveThreads();
            Thread.Sleep(1000);
            LogManager.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Cleanup phase encountered an error.");
        }
        _logger.Info("Exiting application normally...");
    }

    private void LogActiveThreads()
    {
        _logger.Info("=== Listing active threads: Final Cleanup Before Exit ===");
        try
        {
            foreach (ProcessThread t in Process.GetCurrentProcess().Threads)
            {
                _logger.Info(" - Thread ID: {0}, State: {1}, Priority: {2}", t.Id, t.ThreadState, t.PriorityLevel);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not list active threads.");
        }
    }
}