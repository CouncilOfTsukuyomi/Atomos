using Atomos.FileMonitor.Events;
using Atomos.FileMonitor.Interfaces;
using Atomos.FileMonitor.Models;
using CommonLib.Consts;
using NLog;

namespace Atomos.FileMonitor.Services;

public sealed class FileWatcher : IFileWatcher, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly List<FileSystemWatcher> _watchers;
    private readonly IFileQueueProcessor _fileQueueProcessor;
    private readonly IFileProcessor _fileProcessor;
    private bool _disposed;

    public event EventHandler<ExtractionProgressChangedEventArgs> ExtractionProgressChanged
    {
        add => _fileProcessor.ExtractionProgressChanged += value;
        remove => _fileProcessor.ExtractionProgressChanged -= value;
    }
    
    public event EventHandler<FileMovedEvent> FileMoved
    {
        add => _fileQueueProcessor.FileMoved += value;
        remove => _fileQueueProcessor.FileMoved -= value;
    }

    public event EventHandler<FilesExtractedEventArgs> FilesExtracted
    {
        add => _fileQueueProcessor.FilesExtracted += value;
        remove => _fileQueueProcessor.FilesExtracted -= value;
    }

    public FileWatcher(IFileQueueProcessor fileQueueProcessor, IFileProcessor fileProcessor)
    {
        _watchers = new List<FileSystemWatcher>();
        _fileQueueProcessor = fileQueueProcessor;
        _fileProcessor = fileProcessor;
    }

    public async Task StartWatchingAsync(IEnumerable<string> paths)
    {
        await _fileQueueProcessor.LoadStateAsync();

        var distinctPaths = paths.Distinct().ToList();

        foreach (var path in distinctPaths)
        {
            if (!Directory.Exists(path))
            {
                _logger.Warn("Directory does not exist, skipping: {Path}", path);
                continue;
            }

            var watcher = CreateFileWatcher(path);
            _watchers.Add(watcher);

            _logger.Info("Started watching directory: {Path}", path);
        }

        _fileQueueProcessor.StartProcessing();
    }

    private FileSystemWatcher CreateFileWatcher(string path)
    {
        var watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        foreach (var extension in FileExtensionsConsts.AllowedExtensions)
        {
            watcher.Filters.Add($"*{extension}");
        }

        watcher.Filters.Add("*.opdownload");

        watcher.Created += OnCreated;
        watcher.Renamed += OnRenamed;

        return watcher;
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        _logger.Info("File added to queue: {FullPath}", e.FullPath);
        _fileQueueProcessor.EnqueueFile(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _logger.Info("File renamed from {OldFullPath} to {FullPath}", e.OldFullPath, e.FullPath);
        _fileQueueProcessor.RenameFileInQueue(e.OldFullPath, e.FullPath);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _fileQueueProcessor.PersistState();

            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();

            if (_fileQueueProcessor is IDisposable d)
                d.Dispose();

            _logger.Info("All watchers disposed.");
        }

        _disposed = true;
    }
}