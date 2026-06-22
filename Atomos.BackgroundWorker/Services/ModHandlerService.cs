using Atomos.BackgroundWorker.Interfaces;
using CommonLib.Consts;
using CommonLib.Enums;
using CommonLib.Interfaces;
using CommonLib.Models;
using NLog;
using Atomos.FileMonitor.Interfaces;

namespace Atomos.BackgroundWorker.Services;

public class ModHandlerService : IModHandlerService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IModInstallService _modInstallService;
    private readonly IWebSocketServer _webSocketServer;
    private readonly IConfigurationService _configurationService;

    public ModHandlerService(IModInstallService modInstallService, IWebSocketServer webSocketServer, IConfigurationService configurationService)
    {
        _modInstallService = modInstallService;
        _webSocketServer = webSocketServer;
        _configurationService = configurationService;

    }

    public async Task HandleFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or whitespace.", nameof(filePath));

        var fileType = GetFileType(filePath);
        switch (fileType)
        {
            case FileType.ModFile:
                await HandleModFileAsync(filePath);
                break;
            case FileType.PoseFile:
                await HandlePoseFileAsync(filePath);
                break;

            default:
                throw new InvalidOperationException($"Unhandled file type: {fileType}");
        }
    }

    private FileType GetFileType(string filePath)
    {
        var fileExtension = Path.GetExtension(filePath)?.ToLowerInvariant();
        if (FileExtensionsConsts.ModFileTypes.Contains(fileExtension))
            return FileType.ModFile;
        if (FileExtensionsConsts.PoseFileTypes.Contains(fileExtension))
            return FileType.PoseFile;


        throw new NotSupportedException($"Unsupported file extension: {fileExtension}");
    }

    private async Task HandleModFileAsync(string filePath)
    {
        _logger.Info("Handling file: {FilePath}", filePath);
        var taskId = Guid.NewGuid().ToString();

        try
        {
            var installed = await _modInstallService.InstallModAsync(filePath);

            var fileName = Path.GetFileName(filePath);

            if (installed)
            {
                _logger.Info("Successfully installed mod: {FilePath}", filePath);

                var message = WebSocketMessage.CreateStatus(
                    taskId,
                    WebSocketMessageStatus.Completed,
                    $"Installed mod: {fileName}"
                );

                await _webSocketServer.BroadcastToEndpointAsync("/status", message);
                _logger.Info("Broadcasted completion status for {FilePath} to websocket clients.", filePath);
            }
            else
            {
                var message = WebSocketMessage.CreateStatus(
                    taskId,
                    WebSocketMessageStatus.Failed,
                    $"Mod could not be installed: {fileName}"
                );

                await _webSocketServer.BroadcastToEndpointAsync("/status", message);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle file: {FilePath}", filePath);

            var errorMessage = WebSocketMessage.CreateError(
                taskId,
                $"Failed to handle file: {filePath}\n{ex.Message}"
            );

            await _webSocketServer.BroadcastToEndpointAsync("/status", errorMessage);
        }
    }
    private async Task HandlePoseFileAsync(string filePath)
    {
        var taskId = Guid.NewGuid().ToString();
        try
        {
            var destinationFolder = _configurationService.ReturnConfigValue(
                config => config.BackgroundWorker.PoseDestinationPath)?.ToString();


            if (string.IsNullOrWhiteSpace(destinationFolder))
            {
                _logger.Warn("Pose destination path is not configured. Skipping {FilePath}", filePath);
                return;
            }

            Directory.CreateDirectory(destinationFolder);

            var fileName = Path.GetFileName(filePath);
            var destination = Path.Combine(destinationFolder, fileName);

            File.Move(filePath, destination, overwrite: true);
            _logger.Info("Moved pose file to {Destination}", destination);

            var message = WebSocketMessage.CreateStatus(
                taskId,
                WebSocketMessageStatus.Completed,
                $"Pose file moved: {fileName}"
            );
            await _webSocketServer.BroadcastToEndpointAsync("/status", message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to move pose file: {FilePath}", filePath);
            var errorMessage = WebSocketMessage.CreateError(
                taskId,
                $"Failed to move pose file: {filePath}\n{ex.Message}"
            );
            await _webSocketServer.BroadcastToEndpointAsync("/status", errorMessage);

        }
    }
}
