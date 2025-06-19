using System.Reactive;
using ReactiveUI;

namespace PenumbraModForwarder.UI.Interfaces;

public interface ITrayIconController
{
    ReactiveCommand<Unit, Unit> ShowCommand { get; }
    ReactiveCommand<Unit, Unit> ExitCommand { get; }
}