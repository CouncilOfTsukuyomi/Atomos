namespace Atomos.FileMonitor.Events;

public class ExtractionProgressChangedEventArgs : EventArgs
{
    public string TaskId { get; set; }

    public string Message { get; set; }
    public int Progress { get; set; }

    public ExtractionProgressChangedEventArgs(string taskId, string message, int progress)
    {
        TaskId = taskId;
        Message = message;
        Progress = progress;
    }
}