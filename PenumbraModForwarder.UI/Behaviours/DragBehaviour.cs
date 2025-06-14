using System;
using System.Reactive;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactivity;
using NLog;

namespace PenumbraModForwarder.UI.Behaviours;

public class DragBehaviour : Behavior<Control>
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public static readonly StyledProperty<ICommand?> StartDragCommandProperty =
        AvaloniaProperty.Register<DragBehaviour, ICommand?>(nameof(StartDragCommand));

    public static readonly StyledProperty<ICommand?> DragCommandProperty =
        AvaloniaProperty.Register<DragBehaviour, ICommand?>(nameof(DragCommand));

    public static readonly StyledProperty<ICommand?> EndDragCommandProperty =
        AvaloniaProperty.Register<DragBehaviour, ICommand?>(nameof(EndDragCommand));

    public static readonly StyledProperty<ICommand?> ClickCommandProperty =
        AvaloniaProperty.Register<DragBehaviour, ICommand?>(nameof(ClickCommand));

    public ICommand? StartDragCommand
    {
        get => GetValue(StartDragCommandProperty);
        set => SetValue(StartDragCommandProperty, value);
    }

    public ICommand? DragCommand
    {
        get => GetValue(DragCommandProperty);
        set => SetValue(DragCommandProperty, value);
    }

    public ICommand? EndDragCommand
    {
        get => GetValue(EndDragCommandProperty);
        set => SetValue(EndDragCommandProperty, value);
    }

    public ICommand? ClickCommand
    {
        get => GetValue(ClickCommandProperty);
        set => SetValue(ClickCommandProperty, value);
    }

    private bool _isPointerCaptured;
    private IPointer? _capturedPointer;
    private Point _pressPosition;
    private Point _controlStartPosition;
    private Point _clickOffset;
    private bool _hasDragged;
    private const double DragThreshold = 5.0;

    protected override void OnAttached()
    {
        base.OnAttached();
        
        if (AssociatedObject != null)
        {
            AssociatedObject.PointerPressed += OnPointerPressed;
            AssociatedObject.PointerMoved += OnPointerMoved;
            AssociatedObject.PointerReleased += OnPointerReleased;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.PointerPressed -= OnPointerPressed;
            AssociatedObject.PointerMoved -= OnPointerMoved;
            AssociatedObject.PointerReleased -= OnPointerReleased;
        }
        
        base.OnDetaching();
    }

    private Window? GetParentWindow()
    {
        return AssociatedObject?.FindLogicalAncestorOfType<Window>();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (AssociatedObject == null) return;

            var currentPoint = e.GetCurrentPoint(AssociatedObject);
            if (!currentPoint.Properties.IsLeftButtonPressed) return;

            var window = GetParentWindow();
            if (window == null) return;

            // Get the current control position relative to the window
            _controlStartPosition = AssociatedObject.TranslatePoint(new Point(0, 0), window) ?? new Point(0, 0);
            
            // Store where within the control the user clicked
            _clickOffset = currentPoint.Position;
            
            // Store the initial press position (window coordinates)
            _pressPosition = e.GetPosition(window);
            _hasDragged = false;

            _capturedPointer = e.Pointer;
            e.Pointer.Capture(AssociatedObject);
            _isPointerCaptured = true;
            
            _logger.Debug($"Drag started - Control position: {_controlStartPosition}, Click offset: {_clickOffset}, Press position: {_pressPosition}");
            
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception in OnPointerPressed");
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        try
        {
            if (!_isPointerCaptured || AssociatedObject == null || e.Pointer != _capturedPointer)
                return;

            var window = GetParentWindow();
            if (window == null) return;

            var currentPosition = e.GetPosition(window);
            var distance = Point.Distance(_pressPosition, currentPosition);

            if (!_hasDragged && distance > DragThreshold)
            {
                _hasDragged = true;
                
                // Pass the control's current position when starting the drag
                StartDragCommand?.Execute(_controlStartPosition);
                
                _logger.Debug($"Drag threshold exceeded, starting drag at control position: {_controlStartPosition}");
            }

            if (_hasDragged)
            {
                // Calculate the new control position
                // Current mouse position minus the offset where the user clicked within the control
                var newControlPosition = new Point(
                    currentPosition.X - _clickOffset.X,
                    currentPosition.Y - _clickOffset.Y
                );

                DragCommand?.Execute(newControlPosition);
                
                _logger.Debug($"Dragging to: {newControlPosition} (mouse: {currentPosition}, offset: {_clickOffset})");
            }

            e.Handled = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception in OnPointerMoved");
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (!_isPointerCaptured || AssociatedObject == null || e.Pointer != _capturedPointer)
                return;

            e.Pointer.Capture(null);
            _isPointerCaptured = false;
            _capturedPointer = null;

            if (_hasDragged)
            {
                EndDragCommand?.Execute(Unit.Default);
                _logger.Debug("Drag ended");
            }
            else
            {
                ClickCommand?.Execute(null);
                _logger.Debug("Click detected");
            }

            e.Handled = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception in OnPointerReleased");
        }
    }
}