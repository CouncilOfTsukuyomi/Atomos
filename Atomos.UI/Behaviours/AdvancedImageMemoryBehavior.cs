using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AsyncImageLoader;

namespace Atomos.UI.Behaviours;

public class AdvancedImageMemoryBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnableUnloadClearProperty =
        AvaloniaProperty.RegisterAttached<AdvancedImageMemoryBehavior, AdvancedImage, bool>(
            "EnableUnloadClear");

    public static void SetEnableUnloadClear(AvaloniaObject element, bool value)
        => element.SetValue(EnableUnloadClearProperty, value);

    public static bool GetEnableUnloadClear(AvaloniaObject element)
        => element.GetValue(EnableUnloadClearProperty);

    static AdvancedImageMemoryBehavior()
    {
        EnableUnloadClearProperty.Changed.AddClassHandler<AdvancedImage>((img, e) =>
        {
            var enable = e.GetNewValue<bool>();
            if (enable)
            {
                img.DetachedFromVisualTree += OnDetachedFromVisualTree;
            }
            else
            {
                img.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            }
        });
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is AdvancedImage img)
        {
            try
            {
                // Clear Source to allow image loader to release references
                img.Source = null;
                // Hint to release visuals
                (img as Control)?.InvalidateVisual();
            }
            catch (Exception)
            {
                // Ignore cleanup exceptions
            }
        }
    }
}
