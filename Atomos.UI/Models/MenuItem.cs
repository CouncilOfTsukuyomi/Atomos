using Atomos.UI.ViewModels;
using Avalonia.Media;

namespace Atomos.UI.Models;

public class MenuItem
{
    public string Label { get; }
    public StreamGeometry Icon { get; }
    public ViewModelBase ViewModel { get; }

    public MenuItem(string label, StreamGeometry icon, ViewModelBase viewModel)
    {
        Label = label;
        Icon = icon;
        ViewModel = viewModel;
    }
}