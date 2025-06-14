using System.Collections.ObjectModel;
using System.Reactive;
using PenumbraModForwarder.UI.Interfaces;
using PenumbraModForwarder.UI.Services;
using ReactiveUI;
using System.Collections.Specialized;
using System.Linq;
using Notification = PenumbraModForwarder.UI.Models.Notification;
using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using NLog;

namespace PenumbraModForwarder.UI.ViewModels;

public class NotificationHubViewModel : ViewModelBase
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    private readonly INotificationService _notificationService;
    private bool _isVisible = true;
    private bool _isNotificationFlyoutOpen;

    // This is our persistent collection that maintains notification history
    public ObservableCollection<Notification> PersistentNotifications { get; } = new();

    // This still references the live notifications for real-time updates
    public ObservableCollection<Notification> LiveNotifications =>
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

    public int NotificationCount => PersistentNotifications?.Count ?? 0;
    public bool HasNotifications => NotificationCount > 0;

    public ReactiveCommand<Unit, Unit> ToggleNotificationFlyoutCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearAllNotificationsCommand { get; }
    public ReactiveCommand<Notification, Unit> RemoveNotificationCommand { get; }

    public NotificationHubViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService;
        
        _logger.Info("NotificationHubViewModel initializing with service type: {ServiceType}", 
                    _notificationService?.GetType().Name ?? "null");

        ToggleNotificationFlyoutCommand = ReactiveCommand.Create(() =>
        {
            IsNotificationFlyoutOpen = !IsNotificationFlyoutOpen;
            _logger.Debug("Notification flyout toggled to: {IsOpen}", IsNotificationFlyoutOpen);
        });

        ClearAllNotificationsCommand = ReactiveCommand.CreateFromTask(ClearAllNotificationsAsync);

        RemoveNotificationCommand = ReactiveCommand.CreateFromTask<Notification>(RemoveNotificationAsync);

        // Subscribe to live notification changes to add new ones to our persistent collection
        if (_notificationService is NotificationService notificationServiceImpl)
        {
            _logger.Info("Subscribing to NotificationService.Notifications.CollectionChanged");
            notificationServiceImpl.Notifications.CollectionChanged += OnLiveNotificationsChanged;
            
            // Check if there are already notifications and add them
            var existingCount = notificationServiceImpl.Notifications.Count;
            _logger.Info("Found {ExistingCount} existing notifications", existingCount);
            
            // Add any existing notifications to persistent collection
            foreach (var notification in notificationServiceImpl.Notifications)
            {
                if (PersistentNotifications.All(n => n.Id != notification.Id))
                {
                    var persistentNotification = new Notification(
                        notification.Title,
                        notification.Status,
                        notification.Message,
                        _notificationService,
                        notification.ShowProgress,
                        notification.TaskId
                    )
                    {
                        // Set mutable properties after construction
                        Progress = notification.Progress
                    };

                    if (!string.IsNullOrEmpty(notification.ProgressText))
                        persistentNotification.ProgressText = notification.ProgressText;
                    persistentNotification.AnimationState = "fade-in";
                    
                    PersistentNotifications.Add(persistentNotification);
                    _logger.Debug("Added existing notification copy to hub: {Title} (ID: {Id})", 
                                persistentNotification.Title, persistentNotification.Id);
                }
            }
        }
        else
        {
            _logger.Error("Failed to cast NotificationService or service is null");
        }

        // Subscribe to our own persistent notifications for count updates
        PersistentNotifications.CollectionChanged += (sender, args) =>
        {
            _logger.Debug("PersistentNotifications changed: {Action}, Count: {Count}", 
                        args.Action, PersistentNotifications.Count);
            this.RaisePropertyChanged(nameof(NotificationCount));
            this.RaisePropertyChanged(nameof(HasNotifications));
        };
        
        _logger.Info("NotificationHubViewModel initialized");
    }

    private async Task ClearAllNotificationsAsync()
    {
        try
        {
            _logger.Info("Clearing {Count} notifications from hub", PersistentNotifications.Count);
            
            // Animate all notifications to fade-out state
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var notification in PersistentNotifications)
                {
                    notification.AnimationState = "fade-out";
                }
            });

            // Wait for animations to complete
            await Task.Delay(500);

            // Clear the collection
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PersistentNotifications.Clear();
                this.RaisePropertyChanged(nameof(NotificationCount));
                this.RaisePropertyChanged(nameof(HasNotifications));
            });

            // Close flyout after clearing
            IsNotificationFlyoutOpen = false;
            _logger.Info("Successfully cleared all notifications from hub");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error clearing all notifications from hub");
        }
    }

    private async Task RemoveNotificationAsync(Notification notification)
    {
        if (notification == null) 
        {
            _logger.Warn("Attempted to remove null notification");
            return;
        }

        try
        {
            _logger.Debug("Removing notification from hub: {Title} (ID: {Id})", 
                        notification.Title, notification.Id);
            
            // Set the notification to fade-out state
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                notification.AnimationState = "fade-out";
            });

            // Wait for animation to complete
            await Task.Delay(500);

            // Remove the notification
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PersistentNotifications.Remove(notification);
                this.RaisePropertyChanged(nameof(NotificationCount));
                this.RaisePropertyChanged(nameof(HasNotifications));
            });
            
            _logger.Info("Successfully removed notification from hub: {Title}", notification.Title);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error removing notification from hub: {Title}", notification?.Title ?? "unknown");
        }
    }

    private void OnLiveNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _logger.Debug("OnLiveNotificationsChanged triggered: {Action}", e.Action);
        
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            _logger.Info("Processing {Count} new notifications for hub", e.NewItems.Count);
            
            // When new notifications are added to the live collection, add copies to our persistent collection
            foreach (Notification notification in e.NewItems)
            {
                _logger.Debug("Processing notification for hub: {Title} (ID: {Id})", 
                            notification.Title, notification.Id);
                
                // Check if we already have this notification
                if (PersistentNotifications.All(n => n.Id != notification.Id))
                {
                    var persistentNotification = new Notification(
                        notification.Title,
                        notification.Status,
                        notification.Message,
                        _notificationService,
                        notification.ShowProgress,
                        notification.TaskId
                    )
                    {
                        // Set mutable properties after construction
                        Progress = notification.Progress
                    };

                    if (!string.IsNullOrEmpty(notification.ProgressText))
                        persistentNotification.ProgressText = notification.ProgressText;
                    persistentNotification.AnimationState = "fade-in";
                    
                    // Insert at the beginning so newest notifications appear first
                    PersistentNotifications.Insert(0, persistentNotification);
                    _logger.Info("Added new notification copy to hub: {Title} (ID: {Id})", 
                               persistentNotification.Title, persistentNotification.Id);
                }
                else
                {
                    _logger.Debug("Notification already exists in hub, skipping: {Title} (ID: {Id})", 
                                notification.Title, notification.Id);
                }
            }
        }
        else
        {
            _logger.Debug("LiveNotifications action not Add or NewItems is null: {Action}", e.Action);
        }
        // Note: We don't remove from PersistentNotifications when removed from live notifications
        // This allows the hub to maintain the history even after auto-dismiss
    }
}