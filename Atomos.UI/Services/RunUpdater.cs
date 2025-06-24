using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using CommonLib.Interfaces;
using CommonLib.Models;
using NLog;

namespace Atomos.UI.Services;

public class RunUpdater : IRunUpdater
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly IDownloadUpdater _downloadUpdater;

    public RunUpdater(IDownloadUpdater downloadUpdater)
    {
        _downloadUpdater = downloadUpdater;
    }

    /// <summary>
    /// Downloads and extracts the updater, then runs the downloaded file
    /// with the specified arguments.
    /// 
    /// The programToRunAfterInstallation parameter is optional. If null or empty,
    /// no additional program is specified for launch after the update process.
    /// 
    /// After attempting to start the updater, this method checks if the updater 
    /// is indeed running by scanning the system's process list.
    /// </summary>
    public async Task<bool> RunDownloadedUpdaterAsync(
        string versionNumber,
        string gitHubRepo,
        string installationPath,
        bool enableSentry,
        string? programToRunAfterInstallation = null,
        IProgress<DownloadProgress>? progress = null)
    {
        try
        {
            _logger.Info("=== UPDATER EXECUTION STARTED ===");
            _logger.Info("Progress reporter is {ProgressStatus}", progress != null ? "PROVIDED" : "NULL");
            
            progress?.Report(new DownloadProgress { Status = "Starting updater retrieval...", PercentComplete = 0 });
            
            var ct = CancellationToken.None;

            // Create a progress wrapper for the download phase (0-80%)
            IProgress<DownloadProgress>? downloadProgress = null;
            if (progress != null)
            {
                _logger.Info("Creating progress wrapper for updater download phase");
                downloadProgress = new Progress<DownloadProgress>(p => ReportDownloadProgress(p, progress));
            }
            else
            {
                _logger.Warn("No progress reporter available for updater download");
            }

            var updaterExePath = await _downloadUpdater.DownloadAndExtractLatestUpdaterAsync(ct, downloadProgress);

            if (updaterExePath == null)
            {
                _logger.Fatal("Updater retrieval failed. No file to run.");
                progress?.Report(new DownloadProgress { Status = "Updater retrieval failed", PercentComplete = 0 });
                return false;
            }

            progress?.Report(new DownloadProgress { Status = "Preparing updater execution...", PercentComplete = 85 });

            static string EscapeForCmd(string argument)
            {
                // Escape double quotes and wrap only the individual argument in quotes
                return argument.Contains(' ') || argument.Contains('"') 
                    ? $"\"{argument.Replace("\"", "\\\"")}\"" 
                    : argument;
            }

            // Escape and prepare all arguments individually
            var escapedVersion = EscapeForCmd(versionNumber);
            var escapedRepo = EscapeForCmd(gitHubRepo);
            var escapedPath = EscapeForCmd(installationPath);
            var escapedSentry = EscapeForCmd(enableSentry.ToString().ToLowerInvariant());
            var escapedProgramToRun = string.IsNullOrWhiteSpace(programToRunAfterInstallation)
                ? string.Empty
                : EscapeForCmd(programToRunAfterInstallation);

            var arguments = string.Join(" ", new[]
            {
                escapedVersion,
                escapedRepo,
                escapedPath,
                escapedSentry,
                escapedProgramToRun
            }.Where(arg => !string.IsNullOrWhiteSpace(arg)));

            _logger.Info($"Updater retrieved at {updaterExePath}. Attempting to run with arguments: {arguments}");

            progress?.Report(new DownloadProgress { Status = "Launching updater...", PercentComplete = 90 });

            var processStartInfo = new ProcessStartInfo
            {
                FileName = updaterExePath,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    _logger.Error("Failed to start updater process.");
                    progress?.Report(new DownloadProgress { Status = "Failed to start updater", PercentComplete = 0 });
                    return false;
                }

                _logger.Info($"Updater process started with PID: {process.Id}");
                progress?.Report(new DownloadProgress { Status = "Updater process started...", PercentComplete = 95 });
                
                // Don't wait for the process or keep any references to it
                // The updater should run independently
            }

            // Brief wait to allow process to initialise
            await Task.Delay(1000);

            progress?.Report(new DownloadProgress { Status = "Verifying updater is running...", PercentComplete = 98 });

            if (IsUpdaterRunning(updaterExePath))
            {
                _logger.Info("Updater is confirmed running and detached.");
                progress?.Report(new DownloadProgress { Status = "Updater running successfully!", PercentComplete = 100 });
                _logger.Info("=== UPDATER EXECUTION COMPLETED SUCCESSFULLY ===");
                return true;
            }
            else
            {
                _logger.Warn("Updater is not detected in the process list.");
                progress?.Report(new DownloadProgress { Status = "Updater not detected running", PercentComplete = 0 });
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error while attempting to run the updater file.");
            progress?.Report(new DownloadProgress { Status = "Error starting updater", PercentComplete = 0 });
            return false;
        }
    }

    private void ReportDownloadProgress(DownloadProgress downloadProgress, IProgress<DownloadProgress> overallProgress)
    {
        _logger.Debug("=== UPDATER EXECUTION PROGRESS REPORT ===");
        _logger.Debug("Download Progress: {Percent}%", downloadProgress.PercentComplete);
        _logger.Debug("Download Status: {Status}", downloadProgress.Status);
        _logger.Debug("Speed: {Speed}", downloadProgress.FormattedSpeed);
        _logger.Debug("Size: {Size}", downloadProgress.FormattedSize);

        // Map download progress to overall progress (0% to 80% of total)
        var mappedProgress = downloadProgress.PercentComplete * 0.8; // 80% of total progress for download
        
        _logger.Debug("Calculated mapped progress: {MappedProgress}%", mappedProgress);

        var progressToReport = new DownloadProgress
        {
            Status = downloadProgress.Status ?? "Downloading updater...",
            PercentComplete = mappedProgress,
            DownloadSpeedBytesPerSecond = downloadProgress.DownloadSpeedBytesPerSecond,
            ElapsedTime = downloadProgress.ElapsedTime,
            TotalBytes = downloadProgress.TotalBytes,
            DownloadedBytes = downloadProgress.DownloadedBytes,
        };

        _logger.Debug("Reporting updater execution progress to UI...");
        overallProgress.Report(progressToReport);
        _logger.Debug("=== END UPDATER EXECUTION PROGRESS REPORT ===");
    }

    /// <summary>
    /// Checks if the updater is running by matching the file name, without extension, 
    /// against running processes.
    /// </summary>
    private bool IsUpdaterRunning(string updaterExePath)
    {
        var updaterName = Path.GetFileNameWithoutExtension(updaterExePath);
        if (string.IsNullOrEmpty(updaterName))
        {
            _logger.Warn("No valid updater executable name found.");
            return false;
        }

        return Process.GetProcessesByName(updaterName).Any();
    }
}