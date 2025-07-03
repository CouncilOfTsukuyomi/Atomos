using System;
using Atomos.UI.Interfaces;
using ReactiveUI;

namespace Atomos.UI.Models;

/// <summary>
/// Represents a UI notification item with distinct fields for the application's name (Title),
/// a status/level label (Status), and the core message (Message). It also supports progress,
/// visibility, and animation state.
/// </summary>
public class Notification : ReactiveObject
{
    private bool _isVisible;
    private int _progress;
    private string _progressText;
    private bool _showProgress;
    private string _animationState = "fade-in";
    
    public bool IsProgressTextRedundant => !string.IsNullOrEmpty(ProgressText) && ProgressText != Title;
        
    public string Id { get; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; } = DateTime.Now;
    public string Title { get; }
    public string Status { get; }
    public string Message { get; }
    public string TaskId { get; }
        
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
        _showProgress = showProgress;
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
}