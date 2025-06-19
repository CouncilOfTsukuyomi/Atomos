namespace PenumbraModForwarder.UI.Interfaces;

public interface ITaskbarFlashService
{
    /// <summary>
    /// Flashes the taskbar icon to get the user's attention.
    /// </summary>
    void FlashTaskbar();
    
    /// <summary>
    /// Stops flashing the taskbar icon.
    /// </summary>
    void StopFlashing();
}