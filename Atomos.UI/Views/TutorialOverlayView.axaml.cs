using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace Atomos.UI.Views;

public partial class TutorialOverlayView : ReactiveUserControl<TutorialOverlayView>
{
    public TutorialOverlayView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {

        });
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}