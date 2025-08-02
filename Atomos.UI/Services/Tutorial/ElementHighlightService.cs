using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomos.UI.Interfaces.Tutorial;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
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
    private double _originalOpacity;
    private Canvas? _arrowCanvas;
    private Visual? _rootVisual;
    private Path? _currentArrow;
    private ScrollViewer? _trackedScrollViewer;
    private bool _isTrackingScroll;

    public void HighlightElement(string elementName, Visual? rootVisual = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(elementName))
            {
                _logger.Debug("No element name provided for highlighting, skipping");
                return;
            }

            RemoveHighlight();

            var element = FindElementByName(elementName, rootVisual);
            if (element == null)
            {
                _logger.Debug("Element not found: {ElementName}", elementName);
                return;
            }

            _highlightedElement = element;
            _originalOpacity = element.Opacity;
            _rootVisual = rootVisual;

            ApplyHighlight(element);

            _ = ScrollToElementAndShowArrow(element, rootVisual);

            _logger.Debug("Highlighted element: {ElementName}", elementName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error highlighting element: {ElementName}", elementName);
        }
    }

    private async Task ScrollToElementAndShowArrow(Control element, Visual? rootVisual = null)
    {
        try
        {
            await ScrollToElementSmoothly(element, rootVisual);
            await Task.Delay(100);
            ShowArrow(element, rootVisual);
            StartTrackingScroll(element);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in scroll and show arrow sequence");
        }
    }

    private void StartTrackingScroll(Control element)
    {
        try
        {
            if (_isTrackingScroll) return;

            _trackedScrollViewer = FindScrollViewerAncestor(element);
            if (_trackedScrollViewer == null) return;

            _trackedScrollViewer.ScrollChanged += OnScrollChanged;
            _isTrackingScroll = true;

            _logger.Debug("Started tracking scroll changes for element");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error starting scroll tracking");
        }
    }

    private void StopTrackingScroll()
    {
        try
        {
            if (!_isTrackingScroll || _trackedScrollViewer == null) return;

            _trackedScrollViewer.ScrollChanged -= OnScrollChanged;
            _trackedScrollViewer = null;
            _isTrackingScroll = false;

            _logger.Debug("Stopped tracking scroll changes");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error stopping scroll tracking");
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        try
        {
            if (_highlightedElement == null || _currentArrow == null) return;

            // Update arrow position when scrolling occurs
            UpdateArrowPosition(_highlightedElement, _rootVisual);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling scroll change");
        }
    }

    private void UpdateArrowPosition(Control element, Visual? rootVisual = null)
    {
        try
        {
            if (_currentArrow == null) return;

            var mainWindow = GetMainWindow(rootVisual);
            if (mainWindow == null) return;

            var elementPosition = element.TranslatePoint(new Point(0, 0), mainWindow);
            if (!elementPosition.HasValue) return;

            var elementBounds = new Rect(elementPosition.Value, element.Bounds.Size);
            var (arrowX, arrowY, rotation) = CalculateArrowPosition(elementBounds, mainWindow);

            Canvas.SetLeft(_currentArrow, arrowX);
            Canvas.SetTop(_currentArrow, arrowY);
            _currentArrow.RenderTransform = new RotateTransform(rotation);

            _logger.Debug("Updated arrow position to ({X}, {Y}) with rotation {Rotation}", arrowX, arrowY, rotation);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error updating arrow position");
        }
    }

    private void ShowArrow(Control element, Visual? rootVisual = null)
    {
        try
        {
            var mainWindow = GetMainWindow(rootVisual);
            if (mainWindow == null) return;
            
            var elementPosition = element.TranslatePoint(new Point(0, 0), mainWindow);
            if (!elementPosition.HasValue) return;

            var elementBounds = new Rect(elementPosition.Value, element.Bounds.Size);

            _arrowCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };

            _currentArrow = CreateArrowPath();
            var (arrowX, arrowY, rotation) = CalculateArrowPosition(elementBounds, mainWindow);

            Canvas.SetLeft(_currentArrow, arrowX);
            Canvas.SetTop(_currentArrow, arrowY);
            _currentArrow.RenderTransform = new RotateTransform(rotation);

            _arrowCanvas.Children.Add(_currentArrow);
            
            if (mainWindow.Content is Grid grid)
            {
                grid.Children.Add(_arrowCanvas);
                Grid.SetRowSpan(_arrowCanvas, grid.RowDefinitions.Count > 0 ? grid.RowDefinitions.Count : 1);
                Grid.SetColumnSpan(_arrowCanvas, grid.ColumnDefinitions.Count > 0 ? grid.ColumnDefinitions.Count : 1);
            }
            else if (mainWindow.Content is Panel panel)
            {
                panel.Children.Add(_arrowCanvas);
            }

            AnimateArrow(_currentArrow);

            _logger.Debug("Arrow shown pointing to element at ({X}, {Y}) with rotation {Rotation}", arrowX, arrowY, rotation);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error showing arrow");
        }
    }

    private Path CreateArrowPath()
    {
        return new Path
        {
            Fill = Brushes.Orange,
            Stroke = Brushes.DarkOrange,
            StrokeThickness = 2,
            Data = Geometry.Parse("M 0,0 L 20,10 L 0,20 L 5,10 Z"),
            RenderTransformOrigin = RelativePoint.Center,
            Opacity = 0.9
        };
    }
    
    private (double x, double y, double rotation) CalculateArrowPosition(Rect elementBounds, Window mainWindow)
    {
        var windowBounds = new Rect(0, 0, mainWindow.ClientSize.Width, mainWindow.ClientSize.Height);
        var elementCenter = elementBounds.Center;
        
        const double arrowOffset = 40;
        const double arrowSize = 20;
        double arrowX, arrowY, rotation;

        _logger.Debug("Element bounds: {ElementBounds}, Window bounds: {WindowBounds}", elementBounds, windowBounds);
        
        var leftSpace = elementBounds.Left;
        var rightSpace = windowBounds.Width - elementBounds.Right;
        var topSpace = elementBounds.Top;
        var bottomSpace = windowBounds.Height - elementBounds.Bottom;

        var potentialPositions = new List<ArrowPosition>
        {
            new ArrowPosition 
            { 
                X = elementBounds.Right + arrowOffset, 
                Y = elementCenter.Y - arrowSize / 2, 
                Rotation = 180, 
                Space = rightSpace,
                IsValid = rightSpace >= arrowOffset + arrowSize
            },
            new ArrowPosition 
            { 
                X = elementBounds.Left - arrowOffset - arrowSize, 
                Y = elementCenter.Y - arrowSize / 2, 
                Rotation = 0, 
                Space = leftSpace,
                IsValid = leftSpace >= arrowOffset + arrowSize
            },
            new ArrowPosition 
            { 
                X = elementCenter.X - arrowSize / 2, 
                Y = elementBounds.Bottom + arrowOffset, 
                Rotation = 270, 
                Space = bottomSpace,
                IsValid = bottomSpace >= arrowOffset + arrowSize
            },
            new ArrowPosition 
            { 
                X = elementCenter.X - arrowSize / 2, 
                Y = elementBounds.Top - arrowOffset - arrowSize, 
                Rotation = 90, 
                Space = topSpace,
                IsValid = topSpace >= arrowOffset + arrowSize
            }
        };
        
        var validPositions = potentialPositions
            .Where(p => p.IsValid)
            .Where(p => !WillCoverButton(new Rect(p.X, p.Y, arrowSize, arrowSize), mainWindow))
            .OrderByDescending(p => p.Space)
            .ToList();

        var bestPosition = validPositions.FirstOrDefault() ?? 
                          potentialPositions.Where(p => p.IsValid).OrderByDescending(p => p.Space).FirstOrDefault();

        if (bestPosition != null)
        {
            arrowX = bestPosition.X;
            arrowY = bestPosition.Y;
            rotation = bestPosition.Rotation;
        }
        else
        {
            arrowX = elementCenter.X - arrowSize / 2;
            arrowY = elementBounds.Top - arrowOffset - arrowSize;
            rotation = 90;
        }
        
        arrowX = Math.Max(5, Math.Min(arrowX, windowBounds.Width - arrowSize - 5));
        arrowY = Math.Max(5, Math.Min(arrowY, windowBounds.Height - arrowSize - 5));

        _logger.Debug("Arrow position calculated: ({X}, {Y}) with rotation {Rotation}", arrowX, arrowY, rotation);

        return (arrowX, arrowY, rotation);
    }

    private bool WillCoverButton(Rect arrowBounds, Window mainWindow)
    {
        try
        {
            var buttons = GetVisibleButtons(mainWindow);
            
            foreach (var button in buttons)
            {
                if (button == _highlightedElement) continue;
                
                var buttonPosition = button.TranslatePoint(new Point(0, 0), mainWindow);
                if (!buttonPosition.HasValue) continue;
                
                var buttonBounds = new Rect(buttonPosition.Value, button.Bounds.Size);
                
                if (arrowBounds.Intersects(buttonBounds))
                {
                    _logger.Debug("Arrow would cover button: {ButtonName} at {Bounds}", 
                        button.Name ?? button.GetType().Name, buttonBounds);
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.Debug("Error checking button collisions: {Error}", ex.Message);
            return false;
        }
    }

    private List<Button> GetVisibleButtons(Window mainWindow)
    {
        var buttons = new List<Button>();
        var visited = new HashSet<Visual>();
        
        try
        {
            FindButtonsSafely(mainWindow, buttons, visited, 0, maxDepth: 20);
        }
        catch (Exception ex)
        {
            _logger.Debug("Error finding buttons: {Error}", ex.Message);
        }
        
        return buttons.Where(b => b.IsVisible && b.IsEnabled).ToList();
    }

    private void FindButtonsSafely(Visual visual, List<Button> buttons, HashSet<Visual> visited, int depth, int maxDepth)
    {
        if (depth > maxDepth || visited.Contains(visual))
            return;

        visited.Add(visual);

        if (visual is Button button && button.IsVisible && button.IsEnabled)
        {
            buttons.Add(button);
        }

        // Only traverse logical children to avoid infinite loops
        if (visual is ILogical logical)
        {
            foreach (var child in logical.LogicalChildren.OfType<Visual>())
            {
                FindButtonsSafely(child, buttons, visited, depth + 1, maxDepth);
            }
        }
    }

    private void AnimateArrow(Path arrow)
    {
        var pulseAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1000),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Children =
            {
                new KeyFrame
                {
                    Setters = { new Setter(Visual.OpacityProperty, 0.6) },
                    Cue = new Cue(0d)
                },
                new KeyFrame
                {
                    Setters = { new Setter(Visual.OpacityProperty, 1.0) },
                    Cue = new Cue(1d)
                }
            }
        };

        _ = pulseAnimation.RunAsync(arrow);
    }

    private Window? GetMainWindow(Visual? rootVisual = null)
    {
        if (rootVisual is Window window) return window;
        
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }

    private void ApplyHighlight(Control element)
    {
        switch (element)
        {
            case Border border:
                _originalBorder = border.BorderBrush;
                _originalBorderThickness = border.BorderThickness;
                _originalBackground = border.Background;
                    
                border.BorderBrush = Brushes.Orange;
                border.BorderThickness = new Thickness(2);
                border.Background = new SolidColorBrush(Colors.Orange, 0.1);
                break;

            case Button button:
                _originalBorder = button.BorderBrush;
                _originalBorderThickness = button.BorderThickness;
                _originalBackground = button.Background;

                button.BorderBrush = Brushes.Orange;
                button.BorderThickness = new Thickness(3);
                button.Background = new SolidColorBrush(Colors.Orange, 0.2);
                break;

            case TextBox textBox:
                _originalBorder = textBox.BorderBrush;
                _originalBorderThickness = textBox.BorderThickness;
                _originalBackground = textBox.Background;

                textBox.BorderBrush = Brushes.Orange;
                textBox.BorderThickness = new Thickness(2);
                
                if (textBox.Background is SolidColorBrush solidBrush)
                {
                    var highlightColor = Color.FromArgb(40, Colors.Orange.R, Colors.Orange.G, Colors.Orange.B);
                    var originalColor = solidBrush.Color;
                    var blendedColor = BlendColors(originalColor, highlightColor);
                    textBox.Background = new SolidColorBrush(blendedColor);
                }
                else
                {
                    textBox.Background = new SolidColorBrush(Colors.Orange, 0.1);
                }
                break;

            case NumericUpDown numericUpDown:
                _originalBorder = numericUpDown.BorderBrush;
                _originalBorderThickness = numericUpDown.BorderThickness;
                _originalBackground = numericUpDown.Background;

                numericUpDown.BorderBrush = Brushes.Orange;
                numericUpDown.BorderThickness = new Thickness(2);
                break;

            case Grid grid:
                _originalBackground = grid.Background;
                grid.Background = new SolidColorBrush(Colors.Orange, 0.1);
                break;

            case StackPanel stackPanel:
                _originalBackground = stackPanel.Background;
                stackPanel.Background = new SolidColorBrush(Colors.Orange, 0.1);
                break;

            case ListBox listBox:
                _originalBorder = listBox.BorderBrush;
                _originalBorderThickness = listBox.BorderThickness;
                _originalBackground = listBox.Background;

                listBox.BorderBrush = Brushes.Orange;
                listBox.BorderThickness = new Thickness(2);
                listBox.Background = new SolidColorBrush(Colors.Orange, 0.1);
                break;

            default:
                element.Opacity = 0.8;
                _logger.Debug("Applied generic highlight for control type: {Type}", element.GetType().Name);
                break;
        }
    }

    private Color BlendColors(Color baseColor, Color overlayColor)
    {
        var alpha = overlayColor.A / 255.0;
        var invAlpha = 1.0 - alpha;

        return Color.FromArgb(
            (byte)Math.Max(baseColor.A, overlayColor.A),
            (byte)(baseColor.R * invAlpha + overlayColor.R * alpha),
            (byte)(baseColor.G * invAlpha + overlayColor.G * alpha),
            (byte)(baseColor.B * invAlpha + overlayColor.B * alpha)
        );
    }

    public void RemoveHighlight()
    {
        if (_highlightedElement == null) return;

        try
        {
            StopTrackingScroll();
            RestoreOriginalStyles(_highlightedElement);
            RemoveArrow();

            _highlightedElement = null;
            _originalBorder = null;
            _originalBackground = null;
            _originalBorderThickness = new Thickness(0);
            _originalOpacity = 1.0;
            _rootVisual = null;
            _currentArrow = null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error removing highlight");
        }
    }

    private void RemoveArrow()
    {
        if (_arrowCanvas == null) return;

        try
        {
            if (_arrowCanvas.Parent is Panel parentPanel)
            {
                parentPanel.Children.Remove(_arrowCanvas);
            }
            else if (_arrowCanvas.Parent is Border border && border.Child is Panel borderPanel)
            {
                borderPanel.Children.Remove(_arrowCanvas);
            }

            _arrowCanvas = null;
            _logger.Debug("Arrow removed");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error removing arrow");
        }
    }

    private void RestoreOriginalStyles(Control element)
    {
        switch (element)
        {
            case Border border:
                border.BorderBrush = _originalBorder;
                border.BorderThickness = _originalBorderThickness;
                border.Background = _originalBackground;
                break;

            case Button button:
                button.BorderBrush = _originalBorder;
                button.BorderThickness = _originalBorderThickness;
                button.Background = _originalBackground;
                break;

            case TextBox textBox:
                textBox.BorderBrush = _originalBorder;
                textBox.BorderThickness = _originalBorderThickness;
                textBox.Background = _originalBackground;
                break;

            case NumericUpDown numericUpDown:
                numericUpDown.BorderBrush = _originalBorder;
                numericUpDown.BorderThickness = _originalBorderThickness;
                numericUpDown.Background = _originalBackground;
                break;

            case Grid grid:
                grid.Background = _originalBackground;
                break;

            case StackPanel stackPanel:
                stackPanel.Background = _originalBackground;
                break;

            case ListBox listBox:
                listBox.BorderBrush = _originalBorder;
                listBox.BorderThickness = _originalBorderThickness;
                listBox.Background = _originalBackground;
                break;

            default:
                element.Opacity = _originalOpacity;
                break;
        }
    }

    public Rect? GetElementBounds(string elementName, Visual? rootVisual = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(elementName))
            {
                _logger.Debug("No element name provided for bounds calculation, skipping");
                return null;
            }

            var element = FindElementByName(elementName, rootVisual);
            if (element == null) return null;

            var mainWindow = GetMainWindow(rootVisual);
            if (mainWindow == null) return null;

            var position = element.TranslatePoint(new Point(0, 0), mainWindow);
            if (!position.HasValue) return null;

            return new Rect(position.Value, element.Bounds.Size);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting element bounds: {ElementName}", elementName);
            return null;
        }
    }

    private async Task ScrollToElementSmoothly(Control element, Visual? rootVisual = null)
    {
        try
        {
            var scrollViewer = FindScrollViewerAncestor(element);
            if (scrollViewer == null)
            {
                _logger.Debug("No ScrollViewer found for element");
                return;
            }

            var elementPosition = element.TranslatePoint(new Point(0, 0), scrollViewer);
            if (!elementPosition.HasValue)
            {
                _logger.Debug("Could not determine element position for scrolling");
                return;
            }

            var elementBounds = new Rect(elementPosition.Value, element.Bounds.Size);
            var viewportBounds = new Rect(0, 0, scrollViewer.Viewport.Width, scrollViewer.Viewport.Height);

            var desiredScrollX = elementBounds.Center.X - viewportBounds.Width / 2;
            var desiredScrollY = elementBounds.Center.Y - viewportBounds.Height / 2;

            desiredScrollX = Math.Max(0, Math.Min(desiredScrollX, scrollViewer.Extent.Width - viewportBounds.Width));
            desiredScrollY = Math.Max(0, Math.Min(desiredScrollY, scrollViewer.Extent.Height - viewportBounds.Height));

            var currentOffset = scrollViewer.Offset;
            var targetOffset = new Vector(desiredScrollX, desiredScrollY);

            if (Math.Abs(currentOffset.X - targetOffset.X) < 1 && Math.Abs(currentOffset.Y - targetOffset.Y) < 1)
            {
                _logger.Debug("Element already in view, no scrolling needed");
                return;
            }

            _logger.Debug("Scrolling from ({CurrentX}, {CurrentY}) to ({TargetX}, {TargetY})", 
                currentOffset.X, currentOffset.Y, targetOffset.X, targetOffset.Y);

            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(600),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Setters =
                        {
                            new Setter(ScrollViewer.OffsetProperty, currentOffset)
                        },
                        Cue = new Cue(0d)
                    },
                    new KeyFrame
                    {
                        Setters =
                        {
                            new Setter(ScrollViewer.OffsetProperty, targetOffset)
                        },
                        Cue = new Cue(1d)
                    }
                }
            };

            await animation.RunAsync(scrollViewer);

            _logger.Debug("Smoothly scrolled to element at position: ({X}, {Y})", desiredScrollX, desiredScrollY);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error smoothly scrolling to element");
        }
    }

    private ScrollViewer? FindScrollViewerAncestor(Visual element)
    {
        var current = element.GetVisualParent();
        
        while (current != null)
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }
            current = current.GetVisualParent();
        }

        return null;
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

        if (visual is ILogical logical)
        {
            foreach (var child in logical.LogicalChildren.OfType<Visual>())
            {
                var result = FindElementRecursive(child, elementName);
                if (result != null) return result;
            }
        }

        foreach (var child in visual.GetVisualChildren())
        {
            var result = FindElementRecursive(child, elementName);
            if (result != null) return result;
        }

        return null;
    }

    private class ArrowPosition
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Rotation { get; set; }
        public double Space { get; set; }
        public bool IsValid { get; set; }
    }
}