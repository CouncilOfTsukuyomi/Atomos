using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Atomos.FileMonitor.Events;
using Atomos.FileMonitor.Interfaces;
using Atomos.FileMonitor.Models;
using CommonLib.Consts;
using CommonLib.Interfaces;
using NLog;
using SevenZipExtractor;

namespace Atomos.FileMonitor.Services;

public sealed class FileProcessor : IFileProcessor
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly Regex PreDtRegex = new(@"(?i)pre\-?dt", RegexOptions.Compiled);

    private readonly IFileStorage _fileStorage;
    private readonly IConfigurationService _configurationService;
    private readonly object _extractionLock = new();

    public event EventHandler<FileMovedEvent>? FileMoved;
    public event EventHandler<FilesExtractedEventArgs>? FilesExtracted;
    public event EventHandler<ExtractionProgressChangedEventArgs>? ExtractionProgressChanged;
    public event EventHandler<ArchiveContentsInspectedEventArgs>? ArchiveContentsInspected;

    public FileProcessor(IFileStorage fileStorage, IConfigurationService configurationService)
    {
        _fileStorage = fileStorage;
        _configurationService = configurationService;
    }

    public bool IsFileReady(string filePath)
    {
        if (!IsFileFullyDownloaded(filePath))
            return false;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
    
    public async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken, string taskId)
    {
        var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
        var relocateFiles = (bool)_configurationService.ReturnConfigValue(c => c.BackgroundWorker.RelocateFiles);

        if (FileExtensionsConsts.ModFileTypes.Contains(extension))
        {
            var finalFilePath = relocateFiles ? MoveFile(filePath) : filePath;
            var fileName = Path.GetFileName(finalFilePath);

            FileMoved?.Invoke(this,
                new FileMovedEvent(
                    fileName,
                    finalFilePath,
                    Path.GetFileNameWithoutExtension(finalFilePath)
                )
            );
        }
        else if (FileExtensionsConsts.ArchiveFileTypes.Contains(extension))
        {
            if (await ArchiveContainsModFileAsync(filePath, cancellationToken))
            {
                var finalFilePath = relocateFiles ? MoveFile(filePath) : filePath;
                
                var archiveContents = await InspectArchiveAsync(finalFilePath, cancellationToken);
                
                if (archiveContents.Count == 1)
                {
                    _logger.Info(
                        "Single mod file detected in archive {ArchiveFileName}. Auto-installing without prompt.",
                        Path.GetFileName(finalFilePath)
                    );

                    var selected = new List<string> { archiveContents[0].RelativePath };
                    await ExtractSelectedFilesAsync(finalFilePath, selected, cancellationToken, taskId);
                }
                else
                {
                    ArchiveContentsInspected?.Invoke(this,
                        new ArchiveContentsInspectedEventArgs(finalFilePath, archiveContents, taskId));
                }
            }
            else
            {
                _logger.Info(
                    "Archive {FilePath} doesn't contain any recognized mod files; leaving file in place.",
                    filePath
                );
            }
        }
        else
        {
            _logger.Warn("Unhandled file type: {FullPath}", filePath);
        }
    }

    public async Task<List<ArchiveFileInfo>> InspectArchiveAsync(string archivePath, CancellationToken cancellationToken)
    {
        var archiveFiles = new List<ArchiveFileInfo>();

        try
        {
            using var archiveFile = new ArchiveFile(archivePath);
            var skipPreDt = (bool)_configurationService.ReturnConfigValue(c => c.BackgroundWorker.SkipPreDt);

            foreach (var entry in archiveFile.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(entry.FileName)?.ToLowerInvariant() ?? string.Empty;
                var isModFile = FileExtensionsConsts.ModFileTypes.Contains(extension);
                
                if (!isModFile)
                    continue;
                
                var isPreDt = PreDtRegex.IsMatch(entry.FileName) || 
                             entry.FileName.IndexOf("Endwalker", StringComparison.OrdinalIgnoreCase) != -1;
            
            if (skipPreDt && isPreDt)
            {
                _logger.Info("Skipping file from previous update during inspection: {FileName}", entry.FileName);
                continue;
            }

                var fileInfo = new ArchiveFileInfo
                {
                    FileName = Path.GetFileName(entry.FileName),
                    RelativePath = entry.FileName,
                    Size = entry.Size,
                    Extension = extension,
                    IsModFile = isModFile,
                    IsPreDt = isPreDt,
                    LastModified = entry.LastWriteTime
                };

                archiveFiles.Add(fileInfo);
            }

            _logger.Info("Inspected archive {ArchivePath} - found {FileCount} mod files", 
                Path.GetFileName(archivePath), archiveFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error inspecting archive: {ArchivePath}", archivePath);
            throw;
        }

        await Task.Delay(50, cancellationToken);
        return archiveFiles.OrderBy(f => f.RelativePath).ToList();
    }

    public async Task ExtractSelectedFilesAsync(string archivePath, List<string> selectedFileNames, 
        CancellationToken cancellationToken, string taskId)
    {
        var extractedFiles = new ConcurrentBag<string>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        try
        {
            List<Entry> selectedEntries;
            string archiveDirectory;
            
            using (var archiveFile = new ArchiveFile(archivePath))
            {
                archiveDirectory = Path.GetDirectoryName(archivePath) ?? string.Empty;
                
                // Filter entries to only selected files
                selectedEntries = archiveFile.Entries
                    .Where(entry => selectedFileNames.Contains(entry.FileName))
                    .ToList();

                if (!selectedEntries.Any())
                {
                    _logger.Info("No files selected for extraction from: {ArchiveFileName}", Path.GetFileName(archivePath));
                    ExtractionProgressChanged?.Invoke(this,
                        new ExtractionProgressChangedEventArgs(taskId, "No files selected for extraction.", 100));
                    return;
                }

                ExtractionProgressChanged?.Invoke(this,
                    new ExtractionProgressChangedEventArgs(taskId, 
                        $"Starting extraction of {selectedEntries.Count} selected file(s)...", 0));

                var totalFiles = selectedEntries.Count;
                int extractedCount = 0;
                var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
                var extractionStartTime = DateTime.Now;

                await Parallel.ForEachAsync(selectedEntries, options, async (entry, token) =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var destinationPath = Path.Combine(archiveDirectory, entry.FileName);
                        var destinationDirectory = Path.GetDirectoryName(destinationPath);
                        if (destinationDirectory != null)
                            _fileStorage.CreateDirectory(destinationDirectory);

                        var stopwatch = Stopwatch.StartNew();
                        _logger.Info("Starting extraction of selected file: {FileName}", entry.FileName);

                        try
                        {
                            lock (_extractionLock)
                            {
                                entry.Extract(destinationPath);
                            }
                            stopwatch.Stop();
                            _logger.Info(
                                "Completed extraction of {FileName} in {Elapsed:0.000} seconds to {DestPath}",
                                entry.FileName,
                                stopwatch.Elapsed.TotalSeconds,
                                destinationPath
                            );
                            extractedFiles.Add(destinationPath);
                        }
                        catch (Exception ex)
                        {
                            stopwatch.Stop();
                            _logger.Error(ex, 
                                "Failed to extract {FileName} after {Elapsed:0.000} seconds",
                                entry.FileName,
                                stopwatch.Elapsed.TotalSeconds
                            );
                            throw;
                        }

                        var done = Interlocked.Increment(ref extractedCount);
                        int percent = (int)((double)done / totalFiles * 100);
                        ExtractionProgressChanged?.Invoke(this,
                            new ExtractionProgressChangedEventArgs(taskId, 
                                $"Extracted {done} of {totalFiles} selected file(s)...", percent));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var extractionEndTime = DateTime.Now;
                var totalExtractionTime = extractionEndTime - extractionStartTime;

                _logger.Info(
                    "Selected files extracted from {ArchiveFileName} in {TotalTime:0.000} seconds",
                    Path.GetFileName(archivePath),
                    totalExtractionTime.TotalSeconds
                );
            }

            ExtractionProgressChanged?.Invoke(this,
                new ExtractionProgressChangedEventArgs(taskId, "Selective extraction complete.", 100));

            await Task.Delay(100, cancellationToken);

            if (extractedFiles.Any())
            {
                FilesExtracted?.Invoke(this,
                    new FilesExtractedEventArgs(Path.GetFileName(archivePath), extractedFiles.ToList()));

                var shouldDelete = (bool)_configurationService.ReturnConfigValue(c => c.BackgroundWorker.AutoDelete);
                if (shouldDelete)
                {
                    await Task.Delay(500, cancellationToken);
                    DeleteFileWithRetry(archivePath);
                    _logger.Info("Archive deleted after selective extraction: {ArchiveFileName}",
                        Path.GetFileName(archivePath));
                }
            }
        }
        catch (Exception ex) when (ex.Message.Contains("not a known archive type"))
        {
            _logger.Warn("Unrecognized archive format: {ArchiveFilePath}", archivePath);
            DeleteFileWithRetry(archivePath);
            _logger.Info("Deleted invalid archive: {ArchiveFileName}", Path.GetFileName(archivePath));
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Canceled selective extraction of archive: {ArchiveFileName}", Path.GetFileName(archivePath));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during selective extraction of archive: {ArchiveFileName}", Path.GetFileName(archivePath));
        }
    }

    private bool IsFileFullyDownloaded(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                var fileNameNoExtension = Path.GetFileNameWithoutExtension(filePath);
                var searchPattern = fileNameNoExtension + ".*.part";
                var partFiles = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
                if (partFiles.Length > 0)
                {
                    _logger.Debug("Detected part files for {FilePath}. Still downloading.", filePath);
                    return false;
                }
            }
            // Check file size stability
            const int maxChecks = 3;
            long lastSize = -1;
            for (int i = 0; i < maxChecks; i++)
            {
                var fileInfo = new FileInfo(filePath);
                var currentSize = fileInfo.Length;
                if (lastSize == currentSize && currentSize != 0)
                    return true;
                lastSize = currentSize;
                Thread.Sleep(1000);
            }
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error checking download completeness: {FilePath}", filePath);
            return false;
        }
    }

    private async Task<bool> ArchiveContainsModFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var archiveFile = new ArchiveFile(filePath);
            var skipPreDt = (bool)_configurationService.ReturnConfigValue(c => c.BackgroundWorker.SkipPreDt);

            var modEntries = GetModEntries(archiveFile, skipPreDt);
            return modEntries.Any();
        }
        catch (Exception ex) when (ex.Message.Contains("not a known archive type"))
        {
            _logger.Warn("File {FilePath} is not recognized as a valid archive.", filePath);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Archive check canceled for {FilePath}.", filePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error checking for mod files in archive: {FilePath}", filePath);
            return false;
        }
        finally
        {
            await Task.Delay(100, cancellationToken);
        }
    }

    private string MoveFile(string filePath)
    {
        var modPath = (string)_configurationService.ReturnConfigValue(c => c.BackgroundWorker.ModFolderPath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var destinationFolder = Path.Combine(modPath, fileNameWithoutExt);
        _fileStorage.CreateDirectory(destinationFolder);
        var destinationPath = Path.Combine(destinationFolder, Path.GetFileName(filePath));
        _fileStorage.CopyFile(filePath, destinationPath, overwrite: true);
        DeleteFileWithRetry(filePath);
        _logger.Info("File moved from {SourcePath} to {DestinationPath}", filePath, destinationPath);
        return destinationPath;
    }

    private void DeleteFileWithRetry(string filePath, int maxAttempts = 5, int delayMs = 1000)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(delayMs);
                }
            
                _fileStorage.Delete(filePath);
                _logger.Info("Deleted file on attempt {Attempt}: {FilePath}", attempt, filePath);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                _logger.Warn(
                    "Attempt {Attempt} to delete file failed: {FilePath}. Retrying in {Delay}ms...",
                    attempt,
                    filePath,
                    delayMs
                );
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.Warn(ex, 
                    "Attempt {Attempt} to delete file failed: {FilePath}. Retrying...",
                    attempt,
                    filePath
                );
            }
        }
        
        try
        {
            _fileStorage.Delete(filePath);
            _logger.Info("Deleted file on final attempt: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete file after {MaxAttempts} attempts: {FilePath}", maxAttempts, filePath);
        }
    }

    private List<Entry?> GetModEntries(ArchiveFile archiveFile, bool skipPreviousUpdates)
    {
        return archiveFile.Entries
            .Where(entry =>
            {
                var entryExtension = Path.GetExtension(entry.FileName)?.ToLowerInvariant();
                if (!FileExtensionsConsts.ModFileTypes.Contains(entryExtension))
                    return false;

                if (skipPreviousUpdates)
                {
                    if (PreDtRegex.IsMatch(entry.FileName) 
                        || entry.FileName.IndexOf("Endwalker", StringComparison.OrdinalIgnoreCase) != -1)
                    {
                        _logger.Info("Skipping file from previous update: {FileName}", entry.FileName);
                        return false;
                    }
                }
                return true;
            })
            .ToList();
    }
}