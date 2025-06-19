using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PenumbraModForwarder.UI.Views;

public partial class NotificationHubView : UserControl
{
    public NotificationHubView()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}