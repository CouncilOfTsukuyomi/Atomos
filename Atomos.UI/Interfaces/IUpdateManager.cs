using System;
using System.Threading.Tasks;
using Atomos.UI.ViewModels;

namespace Atomos.UI.Interfaces;

public interface IUpdateManager
{
    UpdatePromptViewModel UpdatePromptViewModel { get; }
    bool IsCheckingForUpdates { get; }
    event Action<bool>? IsCheckingForUpdatesChanged;

    Task CheckForUpdatesAsync(string currentVersion);
}