using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.Models;
using Avalonia.Threading;
using CommonLib.Enums;
using CommonLib.Interfaces;
using NLog;
using ReactiveUI;

namespace Atomos.UI.Services;

public class NotificationService : ReactiveObject, INotificationService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<string, (Notification notification, DateTime lastUpdated, CancellationTokenSource cancellation)> _progressNotifications = new();
    private readonly SemaphoreSlim _notificationSemaphore = new(1, 1);

    private const int FadeOutDuration = 3000;
    private const int UpdateInterval = 100;
    private const int MaxNotifications = 3;
    private const int ProgressNotificationTimeoutMs = 3000;
    private const int StaleTimeoutMs = 15000;
    private const int AnimationUpdateIntervalMs = 16; // ~60fps

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

    public async Task ShowNotification(string title, string message, SoundType? soundType = null, int durationSeconds = 4)
    {
        if (!IsNotificationEnabled()) return;

        var notification = CreateNotification(title, "Info", message, showProgress: true);
            
        await AddNotificationAsync(notification);
        await PlaySoundIfRequested(soundType);
            
        _ = AnimateProgressAndRemoveAsync(notification, durationSeconds);
    }

    public async Task ShowErrorNotification(string title, string message, SoundType? soundType = null, int durationSeconds = 6)
    {
        if (!IsNotificationEnabled()) return;

        var notification = CreateNotification(title, "Error", message);
            
        await AddNotificationAsync(notification);
        await PlaySoundIfRequested(soundType);
            
        _ = RemoveNotificationAfterDelayAsync(notification, durationSeconds);
    }

    public async Task UpdateProgress(string taskId, string title, string status, int progress)
    {
        if (!IsNotificationEnabled()) return;

        _logger.Debug("Updating progress for {TaskId} - {Title} to {Status}: Progress: {Progress}",
            taskId, title, status, progress);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var isNew = !_progressNotifications.ContainsKey(taskId);
                
            if (isNew)
            {
                var notification = CreateProgressNotification(title, status, progress, taskId);
                var cancellationToken = new CancellationTokenSource();
                    
                _progressNotifications[taskId] = (notification, DateTime.UtcNow, cancellationToken);
                    
                EnsureRoomForNotificationSync();
                Notifications.Add(notification);
                    
                _ = MonitorProgressNotificationAsync(taskId, cancellationToken.Token);
            }
            else if (_progressNotifications.TryGetValue(taskId, out var existing))
            {
                existing.notification.Progress = progress;
                existing.notification.ProgressText = status;
                _progressNotifications[taskId] = (existing.notification, DateTime.UtcNow, existing.cancellation);
            }
        });
    }

    private bool IsNotificationEnabled() =>
        (bool)_configurationService.ReturnConfigValue(config => config.UI.NotificationEnabled);
    
    private Notification CreateNotification(string title, string status, string message, bool showProgress = false, string taskId = null) =>
        new(title, status, message, this, showProgress, taskId)
        {
            IsVisible = true,
            Progress = 0,
            AnimationState = "fade-in" // Set initial animation state
        };
    
    private Notification CreateProgressNotification(string title, string status, int progress, string taskId) =>
        new(title, "In Progress", "Task in progress...", this, true, taskId)
        {
            IsVisible = true,
            Progress = progress,
            ProgressText = status,
            AnimationState = "fade-in" // Set initial animation state
        };

    private async Task AddNotificationAsync(Notification notification)
    {
        await _notificationSemaphore.WaitAsync();
        try
        {
            await EnsureRoomForNotificationAsync();
            await Dispatcher.UIThread.InvokeAsync(() => Notifications.Add(notification));
        }
        finally
        {
            _notificationSemaphore.Release();
        }
    }

    private async Task PlaySoundIfRequested(SoundType? soundType)
    {
        if (soundType.HasValue)
        {
            try
            {
                await _soundManagerService.PlaySoundAsync(soundType.Value);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to play notification sound");
            }
        }
    }

    private async Task AnimateProgressAndRemoveAsync(Notification notification, int durationSeconds)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var totalDurationMs = durationSeconds * 1000;

            while (true)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

                if (elapsed >= totalDurationMs)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => notification.Progress = 100);
                    break;
                }

                var progressPercent = Math.Min(100, (elapsed / totalDurationMs) * 100);
                await Dispatcher.UIThread.InvokeAsync(() => notification.Progress = (int)Math.Round(progressPercent));

                await Task.Delay(AnimationUpdateIntervalMs);
            }

            await RemoveNotificationAsync(notification);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in progress animation for notification");
        }
    }

    private async Task RemoveNotificationAfterDelayAsync(Notification notification, int durationSeconds)
    {
        await Task.Delay(durationSeconds * 1000);
        await RemoveNotificationAsync(notification);
    }

    private async Task MonitorProgressNotificationAsync(string taskId, CancellationToken cancellationToken)
    {
        try
        {
            var removedAfterComplete = false;
            DateTime? completedAt = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(UpdateInterval, cancellationToken);

                if (!_progressNotifications.TryGetValue(taskId, out var progressData))
                    return;

                var (notification, lastUpdated, _) = progressData;

                // Check for stale timeout
                if ((DateTime.UtcNow - lastUpdated).TotalMilliseconds >= StaleTimeoutMs)
                {
                    RemoveProgressNotification(taskId);
                    return;
                }

                // Check for completion
                if (notification.Progress >= 100)
                {
                    if (!removedAfterComplete)
                    {
                        removedAfterComplete = true;
                        completedAt = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - completedAt.Value).TotalMilliseconds >= ProgressNotificationTimeoutMs)
                    {
                        RemoveProgressNotification(taskId);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error monitoring progress notification for task {TaskId}", taskId);
        }
    }

    private void RemoveProgressNotification(string taskId)
    {
        if (_progressNotifications.TryRemove(taskId, out var progressData))
        {
            progressData.cancellation.Cancel();
            progressData.cancellation.Dispose();
                
            _ = RemoveNotificationAsync(progressData.notification);
        }
    }

    public async Task RemoveNotificationAsync(Notification notification)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => notification.AnimationState = "fade-out");
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => Notifications.Remove(notification));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error removing notification");
        }
    }

    private async Task EnsureRoomForNotificationAsync()
    {
        if (Notifications.Count >= MaxNotifications)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Notifications.Count > 0)
                {
                    var oldestNotification = Notifications[0];
                    oldestNotification.AnimationState = "fade-out";
                    await Task.Delay(500);
                    Notifications.Remove(oldestNotification);
                }
            });
        }
    }

    private void EnsureRoomForNotificationSync()
    {
        if (Notifications.Count >= MaxNotifications && Notifications.Count > 0)
        {
            var oldestNotification = Notifications[0];
            oldestNotification.AnimationState = "fade-out";
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                await Dispatcher.UIThread.InvokeAsync(() => Notifications.Remove(oldestNotification));
            });
        }
    }
}