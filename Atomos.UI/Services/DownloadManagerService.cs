
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

            // Report initial progress
            progress?.Report(new DownloadProgress { Status = "Preparing download..." });

            // Get the plugin that provided this mod
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
            
            // Convert the download URL to a direct download URL
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

            // Get configured download path
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

            // Create a progress wrapper that includes notifications for major milestones
            IProgress<DownloadProgress>? wrappedProgress = null;
            if (progress != null)
            {
                wrappedProgress = new Progress<DownloadProgress>(p => OnDownloadProgressChanged(p, pluginMod.Name, progress));
            }

            // Download the file with retry logic
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

        // Create enhanced status message like your updater
        var enhancedProgress = new DownloadProgress
        {
            Status = CreateRichStatusMessage(downloadProgress, modName),
            PercentComplete = downloadProgress.PercentComplete,
            ElapsedTime = downloadProgress.ElapsedTime,
            DownloadSpeedBytesPerSecond = downloadProgress.DownloadSpeedBytesPerSecond,
            TotalBytes = downloadProgress.TotalBytes,
            DownloadedBytes = downloadProgress.DownloadedBytes
        };

        // Also send updates to notification service for websocket clients if available
        if (downloadProgress.PercentComplete > 0 && !string.IsNullOrEmpty(downloadProgress.FormattedSpeed))
        {
            // This might trigger websocket updates like your updater does
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

        // Report to the original progress handler
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

            // Google Drive conversion
            if (host.Contains("drive.google.com") || host.Contains("docs.google.com"))
            {
                return ConvertGoogleDriveUrl(originalUrl);
            }

            // Mega.nz conversion
            if (host.Contains("mega.nz") || host.Contains("mega.co.nz"))
            {
                return await ConvertMegaUrlAsync(originalUrl);
            }

            // Patreon conversion
            if (host.Contains("patreon.com") || host.Contains("patreonusercontent.com"))
            {
                return await ConvertPatreonUrlAsync(originalUrl);
            }

            // If it's already a direct URL or unknown platform, return as-is
            return originalUrl;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting URL: {Url}", originalUrl);
            return originalUrl; // Fallback to original URL
        }
    }

    private string ConvertGoogleDriveUrl(string url)
    {
        try
        {
            // Convert Google Drive share URLs to direct download
            // From: https://drive.google.com/file/d/FILE_ID/view?usp=sharing
            // To: https://drive.google.com/uc?export=download&id=FILE_ID

            var fileIdMatch = Regex.Match(url, @"/file/d/([a-zA-Z0-9_-]+)");
            if (fileIdMatch.Success)
            {
                var fileId = fileIdMatch.Groups[1].Value;
                var directUrl = $"https://drive.google.com/uc?export=download&id={fileId}";
                _logger.Debug("Converted Google Drive URL: {Original} -> {Direct}", url, directUrl);
                return directUrl;
            }

            // Handle other Google Drive URL formats
            var idMatch = Regex.Match(url, @"[?&]id=([a-zA-Z0-9_-]+)");
            if (idMatch.Success)
            {
                var fileId = idMatch.Groups[1].Value;
                var directUrl = $"https://drive.google.com/uc?export=download&id={fileId}";
                _logger.Debug("Converted Google Drive URL: {Original} -> {Direct}", url, directUrl);
                return directUrl;
            }

            _logger.Warn("Could not extract file ID from Google Drive URL: {Url}", url);
            return url;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting Google Drive URL: {Url}", url);
            return url;
        }
    }

    private async Task<string> ConvertMegaUrlAsync(string url)
    {
        try
        {
            // For Mega.nz, you might need to use their API or SDK
            // This is a placeholder - you'll need to implement the actual Mega API integration
            // The Mega API requires cryptographic operations to get direct download links
            
            _logger.Debug("Mega URL conversion not yet implemented: {Url}", url);
            await _notificationService.ShowNotification(
                "Download error",
                $"Mega URL conversion not yet implemented: {url}",
                SoundType.GeneralChime
            );
            
            // For now, return the original URL
            // You'll need to integrate with Mega's .NET SDK or API
            return url;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting Mega URL: {Url}", url);
            return url;
        }
    }

    private async Task<string> ConvertPatreonUrlAsync(string url)
    {
        try
        {
            // If it's already a direct Patreon CDN URL, return as-is
            if (url.Contains("patreonusercontent.com"))
            {
                return url;
            }

            // For Patreon post URLs, you might need to scrape or use their API
            // This is a placeholder - implement based on your needs
            _logger.Debug("Patreon URL conversion not yet implemented: {Url}", url);
            await _notificationService.ShowNotification(
                "Download error",
                $"Patreon URL conversion not yet implemented: {url}",
                SoundType.GeneralChime
            );
            
            // For now, return the original URL
            return url;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error converting Patreon URL: {Url}", url);
            return url;
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