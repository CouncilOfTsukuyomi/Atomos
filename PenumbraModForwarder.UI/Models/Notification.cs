using System.Reactive;
using PenumbraModForwarder.UI.Interfaces;
using ReactiveUI;

namespace PenumbraModForwarder.UI.Models
{
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
        private readonly INotificationService _notificationService;
        
        public string Title { get; }

        public string Status { get; }
        
        public string Message { get; }
        
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }
        
        public Notification(
            string title,
            string status,
            string message,
            INotificationService notificationService,
            bool showProgress = true)
        {
            Title = title;
            Status = status;
            Message = message;

            _notificationService = notificationService;
            _showProgress = showProgress;
            
            CloseCommand = ReactiveCommand.Create(Close);
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

        private void Close()
        {
            _ = _notificationService.RemoveNotification(this);
        }
    }
}