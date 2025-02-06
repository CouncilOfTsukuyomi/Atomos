using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NLog;
using PenumbraModForwarder.Common.Enums;
using PenumbraModForwarder.Common.Interfaces;
using PenumbraModForwarder.UI.Interfaces;
using PenumbraModForwarder.UI.Models;
using ReactiveUI;

namespace PenumbraModForwarder.UI.Services;

public class NotificationService : ReactiveObject, INotificationService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly object _lock = new();
    private readonly Dictionary<string, Notification> _progressNotifications = new();

    private const int FadeOutDuration = 500;
    private const int UpdateInterval = 100;

    private readonly IConfigurationService _configurationService;
    private readonly ISoundManagerService _soundManagerService;
        
    public ObservableCollection<Notification> Notifications { get; } = new();

    public NotificationService(
        IConfigurationService configurationService,
        ISoundManagerService soundManagerService)
    {
        _configurationService = configurationService;
        _soundManagerService = soundManagerService;
    }
        
    public async Task ShowNotification(string message, SoundType? soundType = null, int durationSeconds = 4)
    {
        if (!(bool)_configurationService.ReturnConfigValue(config => config.UI.NotificationEnabled))
            return;
            
        if (soundType.HasValue)
        {
            _ = _soundManagerService.PlaySoundAsync(soundType.Value);
        }
            
        var notification = new Notification(
            title: "General",
            status: "Info",
            message: message,
            notificationService: this,
            showProgress: true
        );

        lock (_lock)
        {
            // Limit to 3 visible notifications at a time.
            if (Notifications.Count >= 3)
            {
                var oldestNotification = Notifications[0];
                oldestNotification.IsVisible = false;

                Task.Delay(FadeOutDuration).ContinueWith(_ =>
                {
                    lock (_lock)
                    {
                        if (Notifications.Count > 0)
                            Notifications.RemoveAt(0);
                    }
                });
            }

            notification.IsVisible = true;
            notification.Progress = 0;
            Notifications.Add(notification);
        }

        var elapsed = 0;
        var totalMs = durationSeconds * 1000;

        // Update notification progress until time has elapsed or user closes.
        while (elapsed < totalMs && notification.IsVisible)
        {
            await Task.Delay(UpdateInterval);
            elapsed += UpdateInterval;
            notification.Progress = (int)((elapsed / (float)totalMs) * 100);
        }

        await RemoveNotification(notification);
    }
        
    public async Task ShowErrorNotification(
        string errorMessage,
        SoundType? soundType = null,
        int durationSeconds = 6)
    {
        if (!(bool)_configurationService.ReturnConfigValue(config => config.UI.NotificationEnabled))
            return;

        // Parse the provided error message into logLevel, applicationName, messageBody.
        var logLevel = "ERROR";
        var applicationName = "PenumbraModForwarder";
        var messageBody = errorMessage;

        try
        {
            var segments = errorMessage.Split('|');
            if (segments.Length >= 3)
            {
                logLevel = segments[0].Trim().ToUpperInvariant();
                applicationName = segments[1].Trim();
                messageBody = string.Join("|", segments, 2, segments.Length - 2).Trim();
            }
        }
        catch (Exception parseEx)
        {
            _logger.Warn(parseEx, "Failed to parse error message. Using default formatting.");
        }
            
        if (soundType.HasValue)
        {
            _ = _soundManagerService.PlaySoundAsync(soundType.Value);
        }
            
        var notification = new Notification(
            title: applicationName,
            status: logLevel,
            message: messageBody,
            notificationService: this,
            showProgress: true
        );

        lock (_lock)
        {
            // Limit to 3 visible notifications at a time.
            if (Notifications.Count >= 3)
            {
                var oldestNotification = Notifications[0];
                oldestNotification.IsVisible = false;

                Task.Delay(FadeOutDuration).ContinueWith(_ =>
                {
                    lock (_lock)
                    {
                        if (Notifications.Count > 0)
                            Notifications.RemoveAt(0);
                    }
                });
            }

            notification.IsVisible = true;
            notification.Progress = 0;
            Notifications.Add(notification);
        }

        var elapsed = 0;
        var totalMs = durationSeconds * 1000;

        // Track how long the notification remains.
        while (elapsed < totalMs && notification.IsVisible)
        {
            await Task.Delay(UpdateInterval);
            elapsed += UpdateInterval;
            notification.Progress = (int)((elapsed / (float)totalMs) * 100);
        }

        await RemoveNotification(notification);
    }
        
    public async Task UpdateProgress(string title, string status, int progress)
    {
        if (!(bool)_configurationService.ReturnConfigValue(config => config.UI.NotificationEnabled))
            return;

        _logger.Debug("Updating progress for {Title} to {Status}: Progress: {Progress}",
            title, status, progress);

        lock (_lock)
        {
            // Check if we have a notification for this key.
            if (!_progressNotifications.ContainsKey(title))
            {
                // Remove the oldest if we have 3 visible
                if (Notifications.Count >= 3)
                {
                    var oldestNotification = Notifications[0];
                    oldestNotification.IsVisible = false;
                    Task.Delay(FadeOutDuration).ContinueWith(_ =>
                    {
                        lock (_lock)
                        {
                            if (Notifications.Count > 0)
                                Notifications.RemoveAt(0);
                        }
                    });
                }

                var notification = new Notification(
                    title: title,
                    status: "In Progress",
                    message: status,
                    notificationService: this,
                    showProgress: true
                )
                {
                    IsVisible = true
                };

                _progressNotifications[title] = notification;
                Notifications.Add(notification);
            }

            var currentNotification = _progressNotifications[title];
            currentNotification.Progress = progress;
            currentNotification.ProgressText = status;

            // If we've hit 100% progress, fade out the notification
            if (progress >= 100)
            {
                currentNotification.IsVisible = false;
                Task.Delay(FadeOutDuration).ContinueWith(_ =>
                {
                    lock (_lock)
                    {
                        if (Notifications.Contains(currentNotification))
                            Notifications.Remove(currentNotification);

                        _progressNotifications.Remove(title);
                    }
                });
            }
        }
    }
        
    public async Task RemoveNotification(Notification notification)
    {
        notification.IsVisible = false;
        await Task.Delay(FadeOutDuration);

        lock (_lock)
        {
            Notifications.Remove(notification);
        }
    }
}