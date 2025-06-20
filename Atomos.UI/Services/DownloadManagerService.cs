using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            
            string directDownloadUrl = pluginMod.DownloadUrl;
            _logger.Debug("Using provided download URL: {DownloadUrl}", directDownloadUrl);
            // TODO: This stuff should be forced anyways
            // else
            // {
            //     // Use plugin to get download URL from mod page URL
            //     _logger.Debug("Getting download URL from plugin for mod: {ModUrl}", pluginMod.ModUrl);
            //     directDownloadUrl = await _pluginService.GetModDownloadUrlAsync(pluginMod.PluginSource, pluginMod.ModUrl);
            //     
            //     if (string.IsNullOrWhiteSpace(directDownloadUrl))
            //     {
            //         _logger.Warn("No direct download URL found for mod: {ModName}", pluginMod.Name);
            //         await _notificationService.ShowNotification(
            //             "Download not available",
            //             $"Could not find download link for '{pluginMod.Name}'",
            //             SoundType.GeneralChime
            //         );
            //         return;
            //     }
            // }

            _logger.Debug("Direct download URL: {DirectUrl}", directDownloadUrl);

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

            // Download the file
            var result = await _aria2Service.DownloadFileAsync(directDownloadUrl, downloadPath, ct);

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
}