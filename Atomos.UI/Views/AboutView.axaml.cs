using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Atomos.UI.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}