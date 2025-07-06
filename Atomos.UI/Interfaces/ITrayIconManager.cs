namespace Atomos.UI.Interfaces;

public interface ITrayIconManager
{
    void ShowTrayIcon();
    void HideTrayIcon();
    void InitializeTrayIcon();
    void RefreshMenu();
}