using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Atomos.UI.Views;

public partial class InstallView : UserControl
{
    public InstallView()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}