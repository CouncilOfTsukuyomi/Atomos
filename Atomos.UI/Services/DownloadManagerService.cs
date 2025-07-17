using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Atomos.UI.Interfaces;
using CommonLib.Enums;
using CommonLib.Interfaces;
using CommonLib.Models;
using NLog;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace Atomos.UI.Services;

public class DownloadManagerService : IDownloadManagerService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IAria2Service _aria2Service;
    private readonly IConfigurationService _configurationService;
    private readonly INotificationService _notificationService;
    private readonly IPluginService _pluginService;

    public DownloadManagerService(
        IAria2Service aria2Service,
        IConfigurationService configurationService,
        INotificationService notificationService,
        IPluginService pluginService)
    {
        _aria2Service = aria2Service;
        _configurationService = configurationService;
        _notificationService = notificationService;
        _pluginService = pluginService;
    }

    public async Task DownloadModAsync(PluginMod pluginMod, CancellationToken ct = default, IProgress<DownloadProgress>? progress = null)
    {
        if (pluginMod?.ModUrl is not { Length: > 0 })
        {
            _logger.Warn("Cannot download. 'pluginMod' or 'pluginMod.ModUrl' is invalid.");
            progress?.Report(new DownloadProgress { Status = "Invalid download parameters" });
            return;
        }

        try
        {
            _logger.Info("Starting download for mod: {ModName} from plugin source: {PluginSource}", 
                pluginMod.Name, pluginMod.PluginSource);

            progress?.Report(new DownloadProgress { Status = "Preparing download..." });

            var plugin = _pluginService.GetPlugin(pluginMod.PluginSource);
            if (plugin == null)
            {
                _logger.Warn("Plugin {PluginSource} not found or not enabled", pluginMod.PluginSource);
                var errorMsg = $"Plugin '{pluginMod.PluginSource}' is not available or enabled.";
                progress?.Report(new DownloadProgress { Status = errorMsg });
                await _notificationService.ShowNotification(
                    "Plugin not available",
                    errorMsg,
                    SoundType.GeneralChime
                );
                return;
            }
            
            progress?.Report(new DownloadProgress { Status = "Converting download URL..." });
            
            string directDownloadUrl = await ConvertToDirectDownloadUrlAsync(pluginMod.DownloadUrl);
            
            if (string.IsNullOrWhiteSpace(directDownloadUrl))
            {
                _logger.Warn("Could not convert to direct download URL for mod: {ModName}", pluginMod.Name);
                var errorMsg = $"Could not process download URL for '{pluginMod.Name}'";
                progress?.Report(new DownloadProgress { Status = errorMsg });
                await _notificationService.ShowNotification(
                    "Download not available",
                    errorMsg,
                    SoundType.GeneralChime
                );
                return;
            }

            _logger.Debug("Converted to direct download URL: {DirectUrl}", directDownloadUrl);

            progress?.Report(new DownloadProgress { Status = "Getting download path..." });

            await _notificationService.ShowNotification(
                "Download started",
                $"Downloading: {pluginMod.Name}",
                SoundType.GeneralChime
            );

            var configuredPaths = _configurationService.ReturnConfigValue(cfg => cfg.BackgroundWorker.DownloadPath)
                as System.Collections.Generic.List<string>;

            if (configuredPaths is null || !configuredPaths.Any())
            {
                _logger.Warn("No download path configured. Aborting download for {Name}.", pluginMod.Name);
                var errorMsg = "No download path configured in settings.";
                progress?.Report(new DownloadProgress { Status = errorMsg });
                await _notificationService.ShowNotification(
                    "Download failed",
                    errorMsg,
                    SoundType.GeneralChime
                );
                return;
            }

            var downloadPath = configuredPaths.First();

            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }

            progress?.Report(new DownloadProgress { Status = "Starting download...", PercentComplete = 0 });

            IProgress<DownloadProgress>? wrappedProgress = null;
            if (progress != null)
            {
                wrappedProgress = new Progress<DownloadProgress>(p => OnDownloadProgressChanged(p, pluginMod.Name, progress));
            }

            var result = await DownloadWithRetryAsync(directDownloadUrl, downloadPath, pluginMod.Name, ct, wrappedProgress);

            if (result)
            {
                _logger.Info("Successfully downloaded {Name} to {Destination}", pluginMod.Name, downloadPath);
                progress?.Report(new DownloadProgress { Status = "Download completed!", PercentComplete = 100 });
                await _notificationService.ShowNotification(
                    "Download complete",
                    $"Downloaded: {pluginMod.Name}",
                    SoundType.GeneralChime
                );
            }
            else
            {
                _logger.Warn("Download of {Name} did not complete successfully.", pluginMod.Name);
                progress?.Report(new DownloadProgress { Status = "Download failed" });
                await _notificationService.ShowNotification(
                    "Download failed",
                    $"Failed to download: {pluginMod.Name}",
                    SoundType.GeneralChime
                );
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Download canceled for {Name}.", pluginMod.Name);
            progress?.Report(new DownloadProgress { Status = "Download canceled" });
            await _notificationService.ShowNotification(
                "Download canceled",
                $"Download canceled: {pluginMod.Name}",
                SoundType.GeneralChime
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during download of {Name}.", pluginMod.Name);
            progress?.Report(new DownloadProgress { Status = $"Download error: {ex.Message}" });
            await _notificationService.ShowNotification(
                "Download error",
                $"Error downloading {pluginMod.Name}: {ex.Message}",
                SoundType.GeneralChime
            );
        }
    }

    private void OnDownloadProgressChanged(DownloadProgress downloadProgress, string modName, IProgress<DownloadProgress> originalProgress)
    {
        _logger.Debug("=== DOWNLOAD PROGRESS UPDATE ===");
        _logger.Debug("Mod: {ModName}", modName);
        _logger.Debug("Status: {Status}", downloadProgress.Status);
        _logger.Debug("Progress: {Percent}%", downloadProgress.PercentComplete);
        _logger.Debug("Speed: {FormattedSpeed}", downloadProgress.FormattedSpeed);
        _logger.Debug("Size: {FormattedSize}", downloadProgress.FormattedSize);

        var enhancedProgress = new DownloadProgress
        {
            Status = CreateRichStatusMessage(downloadProgress, modName),
            PercentComplete = downloadProgress.PercentComplete,
            ElapsedTime = downloadProgress.ElapsedTime,
            DownloadSpeedBytesPerSecond = downloadProgress.DownloadSpeedBytesPerSecond,
            TotalBytes = downloadProgress.TotalBytes,
            DownloadedBytes = downloadProgress.DownloadedBytes
        };

        if (downloadProgress.PercentComplete > 0 && !string.IsNullOrEmpty(downloadProgress.FormattedSpeed))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _notificationService.UpdateProgress(
                        modName, 
                        enhancedProgress.Status, 
                        downloadProgress.FormattedSpeed, 
                        (int)downloadProgress.PercentComplete
                    );
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to send progress update to notification service");
                }
            });
        }

        originalProgress.Report(enhancedProgress);
        
        _logger.Debug("=== END DOWNLOAD PROGRESS UPDATE ===");
    }

    private static string CreateRichStatusMessage(DownloadProgress progress, string modName)
    {
        if (progress.PercentComplete >= 100)
        {
            return $"Downloaded {modName} successfully!";
        }

        if (progress.TotalBytes > 0 && progress.DownloadSpeedBytesPerSecond > 0)
        {
            return $"Downloading {modName}... {progress.FormattedSize} at {progress.FormattedSpeed}";
        }
        else if (progress.DownloadSpeedBytesPerSecond > 0)
        {
            return $"Downloading {modName} at {progress.FormattedSpeed}";
        }
        else if (progress.TotalBytes > 0)
        {
            return $"Downloading {modName}... {progress.FormattedSize}";
        }
        else if (!string.IsNullOrEmpty(progress.Status))
        {
            return $"{modName}: {progress.Status}";
        }
        else
        {
            return $"Downloading {modName}...";
        }
    }

    private async Task<string> ConvertToDirectDownloadUrlAsync(string originalUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl))
            return originalUrl;

        try
        {
            var uri = new Uri(originalUrl);
            var host = uri.Host.ToLowerInvariant();

            // TODO: These should be moved to their own services
            if (host.Contains("drive.google.com") || host.Contains("docs.google.com"))
            {
                return await ConvertGoogleDriveUrl(originalUrl);
            }

            if (host.Contains("mega.nz") || host.Contains("mega.co.nz"))
            {
                return await ConvertMegaUrlAsync(originalUrl);
            }

            if (host.Contains("patreon.com") || host.Contains("patreonusercontent.com"))
            {
                return await ConvertPatreonUrlAsync(originalUrl);
            }

            if (host.Contains("heliosphere.app"))
            {
                return await ConvertHeliosphereAsync(originalUrl);
            }

            return originalUrl;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting URL: {Url}", originalUrl);
            return originalUrl;
        }
    }

    private async Task<string> ConvertHeliosphereAsync(string url)
    {
        try
        {
            _logger.Debug("Heliosphere URL conversion not yet implemented: {Url}", url);
            await _notificationService.ShowNotification(
                "Download error",
                $"Heliosphere URL conversion not yet implemented: {url}",
                SoundType.GeneralChime
            );
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting Heliosphere URL: {Url}", url);
            return string.Empty;
        }
    }

    private async Task<string> ConvertGoogleDriveUrl(string url)
    {
        try
        {
            _logger.Debug("Google URL conversion not yet implemented: {Url}", url);
            await _notificationService.ShowNotification(
                "Download error",
                $"Google URL conversion not yet implemented: {url}",
                SoundType.GeneralChime
            );
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting Google Drive URL: {Url}", url);
            return string.Empty;
        }
    }

    private async Task<string> ConvertMegaUrlAsync(string url)
    {
        try
        {
            _logger.Debug("Mega URL conversion not yet implemented: {Url}", url);
            await _notificationService.ShowNotification(
                "Download error",
                $"Mega URL conversion not yet implemented: {url}",
                SoundType.GeneralChime
            );
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting Mega URL: {Url}", url);
            return string.Empty;
        }
    }

    private async Task<string> ConvertPatreonUrlAsync(string url)
    {
        try
        {
            if (url.Contains("patreonusercontent.com"))
            {
                return url;
            }

            _logger.Debug("Patreon URL conversion not yet implemented: {Url}", url);
            await _notificationService.ShowNotification(
                "Download error",
                $"Patreon URL conversion not yet implemented: {url}",
                SoundType.GeneralChime
            );
            
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting Patreon URL: {Url}", url);
            return string.Empty;
        }
    }

    private async Task<bool> DownloadWithRetryAsync(string url, string downloadPath, string fileName, CancellationToken ct, IProgress<DownloadProgress>? progress = null)
    {
        const int maxRetries = 3;
        const int delayBetweenRetries = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                progress?.Report(new DownloadProgress { Status = $"Download attempt {attempt}/{maxRetries}..." });
                
                var result = await _aria2Service.DownloadFileAsync(url, downloadPath, ct, progress);
                if (result) return true;

                if (attempt < maxRetries)
                {
                    _logger.Info("Download attempt {Attempt} failed for {FileName}, retrying in {Delay}ms", 
                        attempt, fileName, delayBetweenRetries);
                    progress?.Report(new DownloadProgress { Status = $"Retrying in {delayBetweenRetries/1000} seconds..." });
                    await Task.Delay(delayBetweenRetries, ct);
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.Warn(ex, "Download attempt {Attempt} failed for {FileName}, retrying", attempt, fileName);
                progress?.Report(new DownloadProgress { Status = $"Attempt {attempt} failed, retrying..." });
                await Task.Delay(delayBetweenRetries, ct);
            }
        }

        return false;
    }
}