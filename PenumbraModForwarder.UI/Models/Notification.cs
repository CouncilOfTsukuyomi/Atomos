using System.Reactive;
using System.Threading.Tasks;
using PenumbraModForwarder.UI.Interfaces;
using ReactiveUI;
using System;

namespace PenumbraModForwarder.UI.Models;

/// <summary>
/// Represents a UI notification item with distinct fields for the application's name (Title),
/// a status/level label (Status), and the core message (Message). It also supports progress,
/// visibility, and a close command that removes the notification via the INotificationService.
/// </summary>
public class Notification : ReactiveObject
{
    private bool _isVisible;
    private int _progress;
    private string _progressText;
    private bool _showProgress;
    private string _animationState = "fade-in";
    private readonly INotificationService _notificationService;
    
    public bool IsProgressTextRedundant => !string.IsNullOrEmpty(ProgressText) && ProgressText != Title;
        
    public string Id { get; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; } = DateTime.Now;
    public string Title { get; }
    public string Status { get; }
    public string Message { get; }
    public string TaskId { get; }
        
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
        
    public Notification(
        string title,
        string status,
        string message,
        INotificationService notificationService,
        bool showProgress = true,
        string taskId = null
        )
    {
        Title = title;
        Status = status;
        Message = message;
        TaskId = taskId;

        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _showProgress = showProgress;
            
        CloseCommand = ReactiveCommand.CreateFromTask(CloseAsync);
        
        // Handle errors in the close command
        CloseCommand.ThrownExceptions.Subscribe(ex =>
        {
            System.Diagnostics.Debug.WriteLine($"Error in CloseCommand: {ex.Message}");
        });
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    public bool ShowProgress
    {
        get => _showProgress;
        set => this.RaiseAndSetIfChanged(ref _showProgress, value);
    }

    public int Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }

    public string AnimationState
    {
        get => _animationState;
        set => this.RaiseAndSetIfChanged(ref _animationState, value);
    }

    private async Task CloseAsync()
    {
        try
        {
            if (_notificationService == null)
            {
                throw new InvalidOperationException("Notification service is not available");
            }
            
            await _notificationService.RemoveNotificationAsync(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error closing notification: {ex.Message}");
            throw; // Re-throw so ReactiveCommand can handle it
        }
    }
}