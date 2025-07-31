using System.Runtime.InteropServices;

namespace Atomos.Watchdog.Imports;

public class DllImports
{
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [DllImport("kernel32.dll")]
    internal static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);
    
    [DllImport("kernel32.dll")]
    internal static extern bool FreeConsole();
    
    [DllImport("kernel32.dll")]
    internal static extern bool AllocConsole();
    
    [DllImport("kernel32.dll")]
    internal static extern bool AttachConsole(int dwProcessId);
    
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    
    [DllImport("user32.dll")]
    internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    
    [DllImport("shell32.dll")]
    internal static extern IntPtr Shell_NotifyIcon(int dwMessage, IntPtr lpData);
    
    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const int SW_MINIMIZE = 6;
    internal const uint SWP_HIDEWINDOW = 0x0080;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const int ATTACH_PARENT_PROCESS = -1;
    
    internal delegate bool ConsoleCtrlDelegate(int sig);
}