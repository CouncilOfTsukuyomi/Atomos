using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Windows.Input;

namespace PenumbraModForwarder.UI.Helpers;

public class DropHandler
{
    public static readonly AttachedProperty<ICommand> DropCommandProperty =
        AvaloniaProperty.RegisterAttached<DropHandler, Control, ICommand>(
            "DropCommand");
        
    public static void SetDropCommand(AvaloniaObject element, ICommand value)
    {
        element.SetValue(DropCommandProperty, value);
    }
        
    public static ICommand GetDropCommand(AvaloniaObject element)
    {
        return element.GetValue(DropCommandProperty);
    }
        
    static DropHandler()
    {
        DropCommandProperty.Changed.Subscribe(change =>
        {
            if (change.Sender is Control control)
            {
                control.AddHandler(DragDrop.DropEvent, (sender, e) =>
                {
                    var command = GetDropCommand(control);
                    if (command != null && e.Data is DataObject dataObject)
                    {
                        var fileNames = dataObject.GetFileNames();
                        if (fileNames != null)
                        {
                            if (command.CanExecute(fileNames))
                            {
                                command.Execute(fileNames);
                                e.Handled = true;
                            }
                        }
                    }
                });
            }
        });
    }

}