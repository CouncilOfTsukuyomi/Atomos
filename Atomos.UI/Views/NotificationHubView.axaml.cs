using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Atomos.UI.Views;

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