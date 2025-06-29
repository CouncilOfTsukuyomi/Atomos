using Atomos.FileMonitor.Events;
using Atomos.FileMonitor.Models;

namespace Atomos.FileMonitor.Interfaces;

public interface IFileProcessor
{
    event EventHandler<FileMovedEvent>? FileMoved;
    event EventHandler<FilesExtractedEventArgs>? FilesExtracted;
    event EventHandler<ExtractionProgressChangedEventArgs>? ExtractionProgressChanged;
    event EventHandler<ArchiveContentsInspectedEventArgs>? ArchiveContentsInspected;

    bool IsFileReady(string filePath);
    Task ProcessFileAsync(string filePath, CancellationToken cancellationToken, string taskId);
    Task<List<ArchiveFileInfo>> InspectArchiveAsync(string archivePath, CancellationToken cancellationToken);
    Task ExtractSelectedFilesAsync(string archivePath, List<string> selectedFileNames, CancellationToken cancellationToken, string taskId);
}