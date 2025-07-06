using System;
using System.Reactive;
using ReactiveUI;

namespace Atomos.UI.Interfaces
{
    public interface ITrayIconController
    {
        ReactiveCommand<Unit, Unit> ShowCommand { get; }
        ReactiveCommand<Unit, Unit> ExitCommand { get; }
        ReactiveCommand<Unit, Unit> CheckUpdatesCommand { get; }
        ReactiveCommand<Unit, Unit> RefreshPluginsCommand { get; }

        string GetConnectionStatus();
        string GetVersionInfo();
        int GetActiveNotificationsCount();
    }
}