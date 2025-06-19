using Atomos.FileMonitor.Events;
using Atomos.FileMonitor.Models;

namespace Atomos.FileMonitor.Interfaces;

public interface IFileProcessor
{
    bool IsFileReady(string filePath);

    Task ProcessFileAsync(
        string filePath,
        CancellationToken cancellationToken,
        string taskId
    );
    
    event EventHandler<FileMovedEvent>? FileMoved;
    event EventHandler<FilesExtractedEventArgs>? FilesExtracted;
    event EventHandler<ExtractionProgressChangedEventArgs>? ExtractionProgressChanged;
}