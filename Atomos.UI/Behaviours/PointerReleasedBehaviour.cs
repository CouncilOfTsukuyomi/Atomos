using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace Atomos.UI.Behaviours;

public class PointerReleasedBehaviour : Behavior<Control>
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<PointerReleasedBehaviour, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnAttached()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.PointerReleased += OnPointerReleased;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.PointerReleased -= OnPointerReleased;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Command?.CanExecute(null) == true)
        {
            Command.Execute(null);
        }
    }
}