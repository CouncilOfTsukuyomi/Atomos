using System.Diagnostics;
using NLog;
using PenumbraModForwarder.Common.Interfaces;
using PenumbraModForwarder.Watchdog.Interfaces;

namespace PenumbraModForwarder.Watchdog.Services;

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
        string? programToRunAfterInstallation = null)
    {
        _logger.Info("Starting updater retrieval...");
        var ct = CancellationToken.None;

        var updaterExePath = await _downloadUpdater.DownloadAndExtractLatestUpdaterAsync(ct);

        if (updaterExePath == null)
        {
            _logger.Fatal("Updater retrieval failed. No file to run.");
            return false;
        }

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

        var processStartInfo = new ProcessStartInfo
        {
            FileName = updaterExePath,
            Arguments = arguments,
            UseShellExecute = false
        };

        try
        {
            Process.Start(processStartInfo);
            _logger.Info("Updater process has been started.");

            // Optional: Wait briefly for the new process to initialize
            await Task.Delay(2000);

            if (IsUpdaterRunning(updaterExePath))
            {
                _logger.Info("Updater is confirmed running.");
            }
            else
            {
                _logger.Warn("Updater is not detected in the process list.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error while attempting to run the updater file.");
            return false;
        }
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