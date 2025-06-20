using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Atomos.UI.Interfaces;
using CommonLib.Enums;
using CommonLib.Interfaces;
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

    public async Task DownloadModAsync(PluginMod pluginMod, CancellationToken ct = default)
    {
        if (pluginMod?.ModUrl is not { Length: > 0 })
        {
            _logger.Warn("Cannot download. 'pluginMod' or 'pluginMod.ModUrl' is invalid.");
            return;
        }

        try
        {
            _logger.Info("Starting download for mod: {ModName} from plugin source: {PluginSource}", 
                pluginMod.Name, pluginMod.PluginSource);

            // Get the plugin that provided this mod
            var plugin = _pluginService.GetPlugin(pluginMod.PluginSource);
            if (plugin == null)
            {
                _logger.Warn("Plugin {PluginSource} not found or not enabled", pluginMod.PluginSource);
                await _notificationService.ShowNotification(
                    "Plugin not available",
                    $"Plugin '{pluginMod.PluginSource}' is not available or enabled.",
                    SoundType.GeneralChime
                );
                return;
            }
            
            // Convert the download URL to a direct download URL
            string directDownloadUrl = await ConvertToDirectDownloadUrlAsync(pluginMod.DownloadUrl);
            
            if (string.IsNullOrWhiteSpace(directDownloadUrl))
            {
                _logger.Warn("Could not convert to direct download URL for mod: {ModName}", pluginMod.Name);
                await _notificationService.ShowNotification(
                    "Download not available",
                    $"Could not process download URL for '{pluginMod.Name}'",
                    SoundType.GeneralChime
                );
                return;
            }

            _logger.Debug("Converted to direct download URL: {DirectUrl}", directDownloadUrl);

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
                await _notificationService.ShowNotification(
                    "Download failed",
                    "No download path configured in settings.",
                    SoundType.GeneralChime
                );
                return;
            }

            var downloadPath = configuredPaths.First();

            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }

            // Download the file with retry logic
            var result = await DownloadWithRetryAsync(directDownloadUrl, downloadPath, pluginMod.Name, ct);

            if (result)
            {
                _logger.Info("Successfully downloaded {Name} to {Destination}", pluginMod.Name, downloadPath);
                await _notificationService.ShowNotification(
                    "Download complete",
                    $"Downloaded: {pluginMod.Name}",
                    SoundType.GeneralChime
                );
            }
            else
            {
                _logger.Warn("Download of {Name} did not complete successfully.", pluginMod.Name);
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
            await _notificationService.ShowNotification(
                "Download canceled",
                $"Download canceled: {pluginMod.Name}",
                SoundType.GeneralChime
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during download of {Name}.", pluginMod.Name);
            await _notificationService.ShowNotification(
                "Download error",
                $"Error downloading {pluginMod.Name}: {ex.Message}",
                SoundType.GeneralChime
            );
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

    private async Task<bool> DownloadWithRetryAsync(string url, string downloadPath, string fileName, CancellationToken ct)
    {
        const int maxRetries = 3;
        const int delayBetweenRetries = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await _aria2Service.DownloadFileAsync(url, downloadPath, ct);
                if (result) return true;

                if (attempt < maxRetries)
                {
                    _logger.Info("Download attempt {Attempt} failed for {FileName}, retrying in {Delay}ms", 
                        attempt, fileName, delayBetweenRetries);
                    await Task.Delay(delayBetweenRetries, ct);
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.Warn(ex, "Download attempt {Attempt} failed for {FileName}, retrying", attempt, fileName);
                await Task.Delay(delayBetweenRetries, ct);
            }
        }

        return false;
    }
}