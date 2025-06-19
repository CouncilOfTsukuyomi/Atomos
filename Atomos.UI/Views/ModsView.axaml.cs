using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Atomos.UI.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}