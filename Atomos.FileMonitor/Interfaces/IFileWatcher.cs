using Atomos.FileMonitor.Events;
using Atomos.FileMonitor.Models;

namespace Atomos.FileMonitor.Interfaces;

public interface IFileWatcher : IDisposable
{
    Task StartWatchingAsync(IEnumerable<string> paths);
    event EventHandler<FileMovedEvent> FileMoved;
    event EventHandler<FilesExtractedEventArgs> FilesExtracted;
    event EventHandler<ExtractionProgressChangedEventArgs> ExtractionProgressChanged;
}