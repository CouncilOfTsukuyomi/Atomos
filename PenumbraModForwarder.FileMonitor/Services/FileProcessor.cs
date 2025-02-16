using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using NLog;
using PenumbraModForwarder.Common.Consts;
using PenumbraModForwarder.Common.Interfaces;
using PenumbraModForwarder.FileMonitor.Interfaces;
using PenumbraModForwarder.FileMonitor.Models;
using SevenZipExtractor;

namespace PenumbraModForwarder.FileMonitor.Services;

public sealed class FileProcessor : IFileProcessor
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private static readonly Regex PreDtRegex = new(@"(?i)pre\-?dt", RegexOptions.Compiled);

    private readonly IFileStorage _fileStorage;
    private readonly IConfigurationService _configurationService;
    
    private readonly object _extractionLock = new();

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
            // File is locked by another process
            return false;
        }
    }

    public async Task ProcessFileAsync(
        string filePath,
        CancellationToken cancellationToken,
        EventHandler<FileMovedEvent> onFileMoved,
        EventHandler<FilesExtractedEventArgs> onFilesExtracted)
    {
        var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
        var relocateFiles = (bool)_configurationService.ReturnConfigValue(c => c.BackgroundWorker.RelocateFiles);

        if (FileExtensionsConsts.ModFileTypes.Contains(extension))
        {
            // Moves the file when RelocateFiles is true; otherwise leaves it in place
            var finalFilePath = relocateFiles ? MoveFile(filePath) : filePath;
            var fileName = Path.GetFileName(finalFilePath);

            onFileMoved?.Invoke(
                this,
                new FileMovedEvent(
                    fileName,
                    finalFilePath,
                    Path.GetFileNameWithoutExtension(finalFilePath)
                )
            );
        }
        else if (FileExtensionsConsts.ArchiveFileTypes.Contains(extension))
        {
            // Check if the archive contains a mod file
            if (await ArchiveContainsModFileAsync(filePath, cancellationToken))
            {
                // Moves the archive file when RelocateFiles is true; otherwise leaves it in place
                var finalFilePath = relocateFiles ? MoveFile(filePath) : filePath;
                await ProcessArchiveFileAsync(finalFilePath, cancellationToken, onFilesExtracted);
            }
            else
            {
                _logger.Info(
                    "Archive {FilePath} doesn’t contain any recognized mod files; leaving file in place.",
                    filePath
                );
            }
        }
        else
        {
            _logger.Warn("Unhandled file type: {FullPath}", filePath);
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

                // If size is unchanged between checks, assume download complete
                if (lastSize == currentSize && currentSize != 0)
                    return true;

                lastSize = currentSize;
                Thread.Sleep(1000);
            }

            return false;
        }
        catch (IOException)
        {
            // Either file is locked or there's an issue reading
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

        // Prepare destination folder
        _fileStorage.CreateDirectory(destinationFolder);

        var destinationPath = Path.Combine(destinationFolder, Path.GetFileName(filePath));
        _fileStorage.CopyFile(filePath, destinationPath, overwrite: true);

        // Clean up original file
        DeleteFileWithRetry(filePath);

        _logger.Info("File moved from {SourcePath} to {DestinationPath}", filePath, destinationPath);
        return destinationPath;
    }

    private async Task ProcessArchiveFileAsync(
        string archivePath,
        CancellationToken cancellationToken,
        EventHandler<FilesExtractedEventArgs> onFilesExtracted)
    {
        var extractedFiles = new ConcurrentBag<string>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        var taskIdCounter = 0;

        try
        {
            using (var archiveFile = new ArchiveFile(archivePath))
            {
                var skipPreDt = (bool)_configurationService.ReturnConfigValue(c => c.BackgroundWorker.SkipPreDt);
                var archiveDirectory = Path.GetDirectoryName(archivePath) ?? string.Empty;
                var modEntries = GetModEntries(archiveFile, skipPreDt);

                if (!modEntries.Any())
                {
                    _logger.Info("No mod files found in the archive: {ArchiveFileName}", Path.GetFileName(archivePath));
                    return;
                }

                var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
                var extractionStartTime = DateTime.Now;
                
                await Parallel.ForEachAsync(modEntries, options, async (entry, token) =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var taskId = Interlocked.Increment(ref taskIdCounter);
                        var destinationPath = Path.Combine(archiveDirectory, entry.FileName);
                        var destinationDirectory = Path.GetDirectoryName(destinationPath);
                        if (destinationDirectory != null)
                            _fileStorage.CreateDirectory(destinationDirectory);

                        var stopwatch = Stopwatch.StartNew();
                        _logger.Info("Task {TaskId}: Starting extraction of {FileName}", taskId, entry.FileName);

                        try
                        {
                            lock (_extractionLock)
                            {
                                entry.Extract(destinationPath);
                            }

                            stopwatch.Stop();
                            _logger.Info(
                                "Task {TaskId}: Completed extraction of {FileName} in {Elapsed:0.000} seconds to {DestPath}",
                                taskId,
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
                                "Task {TaskId}: Failed to extract {FileName} after {Elapsed:0.000} seconds",
                                taskId,
                                entry.FileName,
                                stopwatch.Elapsed.TotalSeconds
                            );
                            throw;
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var extractionEndTime = DateTime.Now;
                var totalExtractionTime = extractionEndTime - extractionStartTime;

                _logger.Info(
                    "All files extracted from {ArchiveFileName} in {TotalTime:0.000} seconds",
                    Path.GetFileName(archivePath),
                    totalExtractionTime.TotalSeconds
                );
            }

            await Task.Delay(100, cancellationToken);

            if (extractedFiles.Any())
            {
                onFilesExtracted?.Invoke(
                    this,
                    new FilesExtractedEventArgs(Path.GetFileName(archivePath), extractedFiles.ToList())
                );

                var shouldDelete = (bool)_configurationService.ReturnConfigValue(c => c.BackgroundWorker.AutoDelete);
                if (shouldDelete)
                {
                    DeleteFileWithRetry(archivePath);
                    _logger.Info(
                        "Archive deleted after extraction: {ArchiveFileName}",
                        Path.GetFileName(archivePath)
                    );
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
            _logger.Info("Canceled processing of archive: {ArchiveFileName}", Path.GetFileName(archivePath));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing archive: {ArchiveFileName}", Path.GetFileName(archivePath));
        }
    }

    private void DeleteFileWithRetry(string filePath, int maxAttempts = 3, int delayMs = 500)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _fileStorage.Delete(filePath);
                _logger.Info("Deleted file on attempt {Attempt}: {FilePath}", attempt, filePath);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                _logger.Warn(
                    "Attempt {Attempt} to delete file failed: {FilePath}. Retrying...",
                    attempt,
                    filePath
                );
                Thread.Sleep(delayMs);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to delete file: {FilePath}", filePath);
                throw;
            }
        }

        // Final attempt
        try
        {
            _fileStorage.Delete(filePath);
            _logger.Info("Deleted file on final attempt: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete file after multiple attempts: {FilePath}", filePath);
            throw;
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