using Atomos.FileMonitor.Models;

namespace Atomos.FileMonitor.Events;

public class ArchiveContentsInspectedEventArgs : EventArgs
{
    public string ArchivePath { get; }
    public List<ArchiveFileInfo> Files { get; }
    public string TaskId { get; }

    public ArchiveContentsInspectedEventArgs(string archivePath, List<ArchiveFileInfo> files, string taskId)
    {
        ArchivePath = archivePath;
        Files = files;
        TaskId = taskId;
    }
}