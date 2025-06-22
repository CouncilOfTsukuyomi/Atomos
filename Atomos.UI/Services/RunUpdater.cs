using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using CommonLib.Interfaces;
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
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using (var process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    _logger.Error("Failed to start updater process.");
                    return false;
                }

                _logger.Info($"Updater process started with PID: {process.Id}");
                
                // Don't wait for the process or keep any references to it
                // The updater should run independently
            }

            // Brief wait to allow process to initialise
            await Task.Delay(1000);

            if (IsUpdaterRunning(updaterExePath))
            {
                _logger.Info("Updater is confirmed running and detached.");
                return true;
            }
            else
            {
                _logger.Warn("Updater is not detected in the process list.");
                return false;
            }
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