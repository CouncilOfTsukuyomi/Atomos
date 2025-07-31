using System;
using System.Threading.Tasks;
using Atomos.UI.ViewModels;

namespace Atomos.UI.Interfaces;

public interface IUpdateManager : IDisposable
{
    UpdatePromptViewModel UpdatePromptViewModel { get; }
    bool IsCheckingForUpdates { get; }
    bool HasUpdateAvailable { get; }
    string UpdateAvailableText { get; }
    
    event Action<bool>? IsCheckingForUpdatesChanged;
    event Action<bool>? HasUpdateAvailableChanged;
    event Action? ShowUpdatePromptRequested;
    
    Task CheckForUpdatesAsync(string currentVersion);
    void ShowUpdatePrompt();
}