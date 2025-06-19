using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NLog;

namespace Atomos.UI.Services;

public class TaskbarFlashService : ITaskbarFlashService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private CancellationTokenSource? _flashCancellationTokenSource;
    private bool _isFlashing;

    // Windows API constants
    private const uint FLASHW_STOP = 0;
    private const uint FLASHW_CAPTION = 1;
    private const uint FLASHW_TRAY = 2;
    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMER = 4;
    private const uint FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public void FlashTaskbar()
    {
        if (_isFlashing)
        {
            _logger.Debug("Taskbar is already flashing, ignoring new flash request");
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                {
                    // Check if window is already in foreground
                    if (GetForegroundWindow() == hwnd)
                    {
                        _logger.Debug("Window is already in foreground, skipping flash");
                        return;
                    }

                    _isFlashing = true;
                    _flashCancellationTokenSource?.Cancel();
                    _flashCancellationTokenSource = new CancellationTokenSource();

                    // Try advanced flashing first, fallback to simple if it fails
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await FlashTaskbarAsync(hwnd, _flashCancellationTokenSource.Token);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "Advanced flashing failed, trying simple flash");
                            await SimpleFlashAsync(hwnd, _flashCancellationTokenSource.Token);
                        }
                    });
                }
                else
                {
                    _logger.Warn("Could not get valid main window handle for taskbar flashing");
                }
            }
            else
            {
                _logger.Info("Taskbar flashing is only supported on Windows");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error occurred while trying to flash taskbar");
            _isFlashing = false;
        }
    }

    private async Task FlashTaskbarAsync(IntPtr hwnd, CancellationToken cancellationToken)
    {
        try
        {
            const int flashInterval = 600; // 600ms interval between flashes

            _logger.Info("Starting persistent taskbar flashing until window gains focus");

            while (!cancellationToken.IsCancellationRequested)
            {
                // Verify window is still valid
                if (!IsWindow(hwnd))
                {
                    _logger.Warn("Window handle became invalid during flashing");
                    break;
                }

                // Check if window came to foreground - this is the primary stop condition
                if (GetForegroundWindow() == hwnd)
                {
                    _logger.Info("Window came to foreground, stopping flash");
                    break;
                }

                var fInfo = new FLASHWINFO
                {
                    cbSize = Convert.ToUInt32(Marshal.SizeOf<FLASHWINFO>()),
                    hwnd = hwnd,
                    dwFlags = FLASHW_ALL,
                    uCount = 1,
                    dwTimeout = 0
                };

                bool result = FlashWindowEx(ref fInfo);
                if (!result)
                {
                    _logger.Warn("FlashWindowEx returned false, continuing anyway");
                    // Don't break on single failure, continue trying
                }

                await Task.Delay(flashInterval, cancellationToken);
            }

            // Ensure flashing is stopped
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var stopInfo = new FLASHWINFO
                {
                    cbSize = Convert.ToUInt32(Marshal.SizeOf<FLASHWINFO>()),
                    hwnd = hwnd,
                    dwFlags = FLASHW_STOP,
                    uCount = 0,
                    dwTimeout = 0
                };

                FlashWindowEx(ref stopInfo);
                _logger.Info("Taskbar flashing completed and stopped");
            });
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Taskbar flashing was cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during taskbar flashing");
        }
        finally
        {
            _isFlashing = false;
        }
    }

    private async Task SimpleFlashAsync(IntPtr hwnd, CancellationToken cancellationToken)
    {
        try
        {
            // Simple fallback: use FLASHW_TIMER with unlimited count until stopped
            var fInfo = new FLASHWINFO
            {
                cbSize = Convert.ToUInt32(Marshal.SizeOf<FLASHWINFO>()),
                hwnd = hwnd,
                dwFlags = FLASHW_ALL | FLASHW_TIMER,
                uCount = 0, // 0 means flash until stopped
                dwTimeout = 600 // 600ms interval
            };

            bool result = FlashWindowEx(ref fInfo);
            _logger.Info("Simple persistent flash initiated, result: {Result}", result);

            // Monitor for window focus change
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check if window is still valid
                if (!IsWindow(hwnd))
                {
                    _logger.Warn("Window handle became invalid during simple flashing");
                    break;
                }

                // Check if window came to foreground
                if (GetForegroundWindow() == hwnd)
                {
                    _logger.Info("Window came to foreground, stopping simple flash");
                    break;
                }

                await Task.Delay(500, cancellationToken); // Check every 500ms
            }
            
            // Stop the flashing
            var stopInfo = new FLASHWINFO
            {
                cbSize = Convert.ToUInt32(Marshal.SizeOf<FLASHWINFO>()),
                hwnd = hwnd,
                dwFlags = FLASHW_STOP,
                uCount = 0,
                dwTimeout = 0
            };

            FlashWindowEx(ref stopInfo);
            _logger.Info("Simple flash stopped");
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Simple flash was cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in simple flash fallback");
        }
        finally
        {
            _isFlashing = false;
        }
    }

    public void StopFlashing()
    {
        try
        {
            _flashCancellationTokenSource?.Cancel();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                {
                    var fInfo = new FLASHWINFO
                    {
                        cbSize = Convert.ToUInt32(Marshal.SizeOf<FLASHWINFO>()),
                        hwnd = hwnd,
                        dwFlags = FLASHW_STOP,
                        uCount = 0,
                        dwTimeout = 0
                    };

                    FlashWindowEx(ref fInfo);
                    _logger.Info("Taskbar flashing stopped manually");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error occurred while trying to stop taskbar flashing");
        }
        finally
        {
            _isFlashing = false;
        }
    }

    private IntPtr GetMainWindowHandle()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            // Get the platform handle from the main window
            if (desktop.MainWindow.TryGetPlatformHandle() is { } platformHandle)
            {
                return platformHandle.Handle;
            }
        }

        return IntPtr.Zero;
    }
}