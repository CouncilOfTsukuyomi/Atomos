using System;
using System.Linq;
using Atomos.UI.Interfaces.Tutorial;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using NLog;

namespace Atomos.UI.Services.Tutorial;

public class ElementHighlightService : IElementHighlightService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private Control? _highlightedElement;
    private IBrush? _originalBorder;
    private Thickness _originalBorderThickness;
    private IBrush? _originalBackground;

    public void HighlightElement(string elementName, Visual? rootVisual = null)
    {
        try
        {
            RemoveHighlight();

            var element = FindElementByName(elementName, rootVisual);
            if (element == null)
            {
                _logger.Debug("Element not found: {ElementName}", elementName);
                return;
            }

            _highlightedElement = element;
                
            // Store original styling
            if (element is Border border)
            {
                _originalBorder = border.BorderBrush;
                _originalBorderThickness = border.BorderThickness;
                _originalBackground = border.Background;
                    
                // Apply highlight
                border.BorderBrush = Brushes.Orange;
                border.BorderThickness = new Thickness(2);
                border.Background = new SolidColorBrush(Colors.Orange, 0.1);
            }
            else if (element is Control control)
            {
                // For other controls, we can try to add a highlight border
                // This would require creating a wrapper or using attached properties
                _logger.Debug("Highlighting control of type: {Type}", control.GetType().Name);
            }

            _logger.Debug("Highlighted element: {ElementName}", elementName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error highlighting element: {ElementName}", elementName);
        }
    }

    public void RemoveHighlight()
    {
        if (_highlightedElement == null) return;

        try
        {
            if (_highlightedElement is Border border)
            {
                border.BorderBrush = _originalBorder;
                border.BorderThickness = _originalBorderThickness;
                border.Background = _originalBackground;
            }

            _highlightedElement = null;
            _originalBorder = null;
            _originalBackground = null;
            _originalBorderThickness = new Thickness(0);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error removing highlight");
        }
    }

    public Rect? GetElementBounds(string elementName, Visual? rootVisual = null)
    {
        try
        {
            var element = FindElementByName(elementName, rootVisual);
            if (element == null) return null;

            var bounds = element.Bounds;
            var position = element.TranslatePoint(new Point(0, 0), rootVisual);
                
            if (position.HasValue)
            {
                return new Rect(position.Value, bounds.Size);
            }

            return bounds;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting element bounds: {ElementName}", elementName);
            return null;
        }
    }

    private Control? FindElementByName(string elementName, Visual? rootVisual = null)
    {
        try
        {
            rootVisual ??= Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (rootVisual == null) return null;

            return FindElementRecursive(rootVisual, elementName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error finding element by name: {ElementName}", elementName);
            return null;
        }
    }

    private Control? FindElementRecursive(Visual visual, string elementName)
    {
        if (visual is Control control && control.Name == elementName)
        {
            return control;
        }

        // Check logical children first
        if (visual is ILogical logical)
        {
            foreach (var child in logical.LogicalChildren.OfType<Visual>())
            {
                var result = FindElementRecursive(child, elementName);
                if (result != null) return result;
            }
        }

        // Then check visual children
        foreach (var child in visual.GetVisualChildren())
        {
            var result = FindElementRecursive(child, elementName);
            if (result != null) return result;
        }

        return null;
    }
}