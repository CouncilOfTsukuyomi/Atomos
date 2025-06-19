using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Atomos.UI.Views;

public partial class SentryPromptView : UserControl
{
    public SentryPromptView()
    {
        InitializeComponent();
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}