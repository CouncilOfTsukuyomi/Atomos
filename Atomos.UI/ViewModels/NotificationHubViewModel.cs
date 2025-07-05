using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.Services;
using Avalonia;
using CommonLib.Events;
using CommonLib.Interfaces;
using NLog;
using ReactiveUI;
using UINotification = Atomos.UI.Models.Notification;

namespace Atomos.UI.ViewModels;

public class NotificationHubViewModel : ViewModelBase
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly INotificationService _notificationService;
    private readonly IConfigurationService _configurationService;
    
    private bool _isVisible = true;
    private bool _isNotificationFlyoutOpen;
    private bool _showNotificationButton = true;
    private double _buttonX;
    private double _buttonY;
    private double _flyoutX = 20;
    private double _flyoutY = 120;
    private bool _isDragging = false;
    
    // Window bounds tracking
    private Size _parentBounds = new Size(800, 600); // Default fallback
    private const double TitleBarHeight = 30; // Height of title bar
    private const double ButtonSize = 50; // Width/Height of notification button
    private const double Padding = 10; // Minimum distance from edges

    public ObservableCollection<UINotification> PersistentNotifications { get; } = new();
    
    public ObservableCollection<UINotification> LiveNotifications =>
        (_notificationService as NotificationService)?.Notifications ?? new();

    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    public bool IsNotificationFlyoutOpen
    {
        get => _isNotificationFlyoutOpen;
        set => this.RaiseAndSetIfChanged(ref _isNotificationFlyoutOpen, value);
    }
    
    public bool ShowNotificationButton
    {
        get => _showNotificationButton;
        set => this.RaiseAndSetIfChanged(ref _showNotificationButton, value);
    }


    public double ButtonX
    {
        get => _buttonX;
        set 
        {
            var constrainedX = ConstrainButtonX(value);
            this.RaiseAndSetIfChanged(ref _buttonX, constrainedX);
            this.RaisePropertyChanged(nameof(ButtonMargin));
            UpdateFlyoutPosition();
            if (!IsDragging) // Only save when not dragging
            {
                SavePosition();
            }
        }
    }

    public double ButtonY
    {
        get => _buttonY;
        set 
        {
            var constrainedY = ConstrainButtonY(value);
            this.RaiseAndSetIfChanged(ref _buttonY, constrainedY);
            this.RaisePropertyChanged(nameof(ButtonMargin));
            UpdateFlyoutPosition();
            if (!IsDragging) // Only save when not dragging
            {
                SavePosition();
            }
        }
    }

    public double FlyoutX
    {
        get => _flyoutX;
        set 
        {
            this.RaiseAndSetIfChanged(ref _flyoutX, value);
            this.RaisePropertyChanged(nameof(FlyoutMargin));
        }
    }

    public double FlyoutY
    {
        get => _flyoutY;
        set 
        {
            this.RaiseAndSetIfChanged(ref _flyoutY, value);
            this.RaisePropertyChanged(nameof(FlyoutMargin));
        }
    }

    public bool IsDragging
    {
        get => _isDragging;
        set => this.RaiseAndSetIfChanged(ref _isDragging, value);
    }

    public Thickness ButtonMargin => new Thickness(ButtonX, ButtonY, 0, 0);
    public Thickness FlyoutMargin => new Thickness(FlyoutX, FlyoutY, 0, 0);
    public int NotificationCount => PersistentNotifications?.Count ?? 0;
    public bool HasNotifications => NotificationCount > 0;

    public ReactiveCommand<Unit, Unit> ToggleNotificationFlyoutCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearAllNotificationsCommand { get; }
    public ReactiveCommand<UINotification, Unit> RemoveNotificationCommand { get; }
    public ReactiveCommand<Point, Unit> StartDragCommand { get; }
    public ReactiveCommand<Point, Unit> DragCommand { get; }
    public ReactiveCommand<Unit, Unit> EndDragCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseNotificationFlyoutCommand { get; }

    private Point _dragStartPosition;
    private Point _buttonStartPosition;

    public NotificationHubViewModel(INotificationService notificationService, IConfigurationService configurationService)
    {
        _notificationService = notificationService;
        _configurationService = configurationService;
        
        _logger.Info($"NotificationHubViewModel initializing with service type: {_notificationService?.GetType().Name ?? "null"}");
        
        LoadConfiguration();
        
        _configurationService.ConfigurationChanged += OnConfigurationChanged;
        
        // Load saved position
        LoadPosition();

        ToggleNotificationFlyoutCommand = ReactiveCommand.Create(() =>
        {
            _logger.Debug($"Toggling notification flyout. Current state: {IsNotificationFlyoutOpen}");
            IsNotificationFlyoutOpen = !IsNotificationFlyoutOpen;
            _logger.Debug($"New flyout state: {IsNotificationFlyoutOpen}");
        });
        
        CloseNotificationFlyoutCommand = ReactiveCommand.Create(() =>
        {
            _logger.Debug("Closing notification flyout via background click");
            IsNotificationFlyoutOpen = false;
        });

        ClearAllNotificationsCommand = ReactiveCommand.CreateFromTask(ClearAllNotificationsAsync);
        RemoveNotificationCommand = ReactiveCommand.CreateFromTask<UINotification>(RemoveNotificationAsync);

        // Drag commands
        StartDragCommand = ReactiveCommand.Create<Point>(StartDrag);
        DragCommand = ReactiveCommand.Create<Point>(HandleDrag);
        EndDragCommand = ReactiveCommand.Create(EndDrag);

        // Subscribe to live notification changes to add new ones to our persistent collection
        if (_notificationService is NotificationService notificationServiceImpl)
        {
            notificationServiceImpl.Notifications.CollectionChanged += OnLiveNotificationsChanged;
            
            // Add any existing notifications as persistent copies
            foreach (var notification in notificationServiceImpl.Notifications)
            {
                var persistentNotification = CreatePersistentCopy(notification);
                if (!PersistentNotifications.Any(n => n.Id == persistentNotification.Id))
                {
                    _logger.Debug($"Adding existing notification to persistent collection: {notification.Title}");
                    PersistentNotifications.Add(persistentNotification);
                }
            }
        }
        else
        {
            _logger.Warn("NotificationService is not of expected type");
        }

        // Subscribe to our own persistent notifications for count updates
        PersistentNotifications.CollectionChanged += (sender, args) =>
        {
            this.RaisePropertyChanged(nameof(NotificationCount));
            this.RaisePropertyChanged(nameof(HasNotifications));
        };
        
        _logger.Info("NotificationHubViewModel initialized");
    }
    
    private void LoadConfiguration()
    {
        try
        {
            var showButton = (bool)_configurationService.ReturnConfigValue(c => c.UI.ShowNotificationButton);
            ShowNotificationButton = showButton;
            _logger.Debug($"Loaded ShowNotificationButton setting: {showButton}");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to load ShowNotificationButton setting, using default");
            ShowNotificationButton = true;
        }
    }

    private void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        if (e.PropertyName == "UI.ShowNotificationButton" && e.NewValue is bool showButton)
        {
            _logger.Debug($"ShowNotificationButton changed to: {showButton}");
            ShowNotificationButton = showButton;
        }
    }


    private void LoadPosition()
    {
        try
        {
            // Try to load from configuration 
            var savedX = (double)_configurationService.ReturnConfigValue(c => c.UI.NotificationButtonX);
            var savedY = (double)_configurationService.ReturnConfigValue(c => c.UI.NotificationButtonY);
            
            // Set directly to backing fields to avoid triggering save during load
            _buttonX = savedX > 0 ? savedX : 20; // Default to 20 if not set
            _buttonY = savedY > 0 ? savedY : 60; // Default to 60 if not set
            
            // Raise property changed for margin calculation
            this.RaisePropertyChanged(nameof(ButtonX));
            this.RaisePropertyChanged(nameof(ButtonY));
            this.RaisePropertyChanged(nameof(ButtonMargin));
            
            _logger.Debug($"Loaded notification button position: {_buttonX}, {_buttonY}");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to load notification button position, using defaults");
            _buttonX = 20;
            _buttonY = 60;
            this.RaisePropertyChanged(nameof(ButtonX));
            this.RaisePropertyChanged(nameof(ButtonY));
            this.RaisePropertyChanged(nameof(ButtonMargin));
        }
        
        UpdateFlyoutPosition();
    }

    private async void SavePosition()
    {
        try
        {
            // Save to configuration using your pattern
            _configurationService.UpdateConfigValue(
                c => c.UI.NotificationButtonX = ButtonX,
                "UI.NotificationHubButtonX",
                ButtonX
            );
            
            _configurationService.UpdateConfigValue(
                c => c.UI.NotificationButtonY = ButtonY,
                "UI.NotificationHubButtonY", 
                ButtonY
            );
            
            _logger.Debug($"Saved notification button position: {ButtonX}, {ButtonY}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save notification button position");
        }
    }

    private void StartDrag(Point position)
    {
        _logger.Debug($"Starting drag at position: {position.X}, {position.Y}");
        IsDragging = true;
        _dragStartPosition = position;
        // Store the current button position as our reference
        _buttonStartPosition = new Point(ButtonX, ButtonY);
    }

    private void HandleDrag(Point currentPosition)
    {
        if (!IsDragging) return;

        _logger.Debug($"Dragging to: {currentPosition.X}, {currentPosition.Y}");

        // Use the property setters instead of backing fields to trigger UI updates
        ButtonX = currentPosition.X;
        ButtonY = currentPosition.Y;
    }

    private void EndDrag()
    {
        _logger.Debug($"Ending drag at position: {ButtonX}, {ButtonY}");
        IsDragging = false;
        
        // Save position when drag ends
        SavePosition();
    }

    private double ConstrainButtonX(double x)
    {
        var minX = Padding;
        var maxX = _parentBounds.Width - ButtonSize - Padding;
        var constrained = Math.Max(minX, Math.Min(maxX, x));
        
        if (constrained != x)
        {
            _logger.Debug($"Constraining X from {x} to {constrained} (bounds: {minX} to {maxX})");
        }
        
        return constrained;
    }

    private double ConstrainButtonY(double y)
    {
        var minY = TitleBarHeight + Padding; // Don't go above title bar
        var maxY = _parentBounds.Height - ButtonSize - Padding;
        var constrained = Math.Max(minY, Math.Min(maxY, y));
        
        if (constrained != y)
        {
            _logger.Debug($"Constraining Y from {y} to {constrained} (bounds: {minY} to {maxY})");
        }
        
        return constrained;
    }

    private void UpdateFlyoutPosition()
    {
        const double flyoutWidth = 350;
        const double flyoutOffset = 10;

        // Try to position flyout to the right of the button
        var preferredX = ButtonX + ButtonSize + flyoutOffset;
    
        // If it would go off screen, position it to the left
        double newFlyoutX;
        if (preferredX + flyoutWidth > _parentBounds.Width - Padding)
        {
            newFlyoutX = Math.Max(Padding, ButtonX - flyoutWidth - flyoutOffset);
        }
        else
        {
            newFlyoutX = preferredX;
        }

        // Position flyout vertically aligned with button, but constrain to screen
        var preferredY = ButtonY;
        var maxFlyoutHeight = 200;
    
        double newFlyoutY;
        if (preferredY + maxFlyoutHeight > _parentBounds.Height - Padding)
        {
            // If flyout would go off the bottom, position it so it fits within the screen
            newFlyoutY = _parentBounds.Height - maxFlyoutHeight - Padding;
        }
        else
        {
            // Otherwise, align it with the button
            newFlyoutY = preferredY;
        }
    
        // Ensure flyout doesn't go above the title bar, but only as a minimum constraint
        newFlyoutY = Math.Max(TitleBarHeight + Padding, newFlyoutY);

        _logger.Debug($"Updating flyout position from ({FlyoutX}, {FlyoutY}) to ({newFlyoutX}, {newFlyoutY})");

        // Use the property setters to trigger UI updates
        FlyoutX = newFlyoutX;
        FlyoutY = newFlyoutY;
    }

    public void SetParentBounds(Size bounds)
    {
        _logger.Debug($"Setting parent bounds to: {bounds.Width} x {bounds.Height}");
        _parentBounds = bounds;
        
        // Re-constrain current position within new bounds
        var constrainedX = ConstrainButtonX(ButtonX);
        var constrainedY = ConstrainButtonY(ButtonY);
        
        // Update if position changed due to constraints
        if (Math.Abs(constrainedX - ButtonX) > 0.1 || Math.Abs(constrainedY - ButtonY) > 0.1)
        {
            ButtonX = constrainedX;
            ButtonY = constrainedY;
        }
        
        UpdateFlyoutPosition();
    }

    private async Task ClearAllNotificationsAsync()
    {
        _logger.Debug("Clearing all notifications");
        PersistentNotifications.Clear();
        await Task.CompletedTask;
    }

    private async Task RemoveNotificationAsync(UINotification notification)
    {
        _logger.Debug($"Removing notification: {notification.Title}");
        PersistentNotifications.Remove(notification);
        await Task.CompletedTask;
    }

    private void OnLiveNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (UINotification notification in e.NewItems)
            {
                // Create a copy of the notification for persistence to avoid reference issues
                var persistentNotification = CreatePersistentCopy(notification);
                
                // Check if we already have a notification with the same ID
                if (!PersistentNotifications.Any(n => n.Id == persistentNotification.Id))
                {
                    _logger.Debug($"Adding new notification to persistent collection: {notification.Title}");
                    PersistentNotifications.Add(persistentNotification);
                }
            }
        }
        
        // Handle removed items - we don't remove from persistent collection
        // as we want to keep a history of notifications
        if (e.OldItems != null)
        {
            _logger.Debug($"Live notifications removed: {e.OldItems.Count} items");
        }
    }
    
    private UINotification CreatePersistentCopy(UINotification original)
    {
        var copy = new UINotification(
            original.Title,
            original.Status,
            original.Message,
            _notificationService,
            original.ShowProgress,
            original.TaskId
        );
        
        copy.IsVisible = original.IsVisible;
        copy.Progress = original.Progress;
        copy.ProgressText = original.ProgressText;
        copy.AnimationState = original.AnimationState;
        
        return copy;
    }
    
    public void Dispose()
    {
        _configurationService.ConfigurationChanged -= OnConfigurationChanged;
    }

}