﻿using CommonLib.Consts;
using CommonLib.Enums;
using CommonLib.Interfaces;
using CommonLib.Models;
using NLog;
using PenumbraModForwarder.BackgroundWorker.Interfaces;

namespace PenumbraModForwarder.BackgroundWorker.Services;

public class ModHandlerService : IModHandlerService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IModInstallService _modInstallService;
    private readonly IWebSocketServer _webSocketServer;

    public ModHandlerService(IModInstallService modInstallService, IWebSocketServer webSocketServer)
    {
        _modInstallService = modInstallService;
        _webSocketServer = webSocketServer;
    }

    public async Task HandleFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be null or whitespace.", nameof(filePath));
        }

        var fileType = GetFileType(filePath);
        switch (fileType)
        {
            case FileType.ModFile:
                await HandleModFileAsync(filePath);
                break;

            default:
                throw new InvalidOperationException($"Unhandled file type: {fileType}");
        }
    }

    private FileType GetFileType(string filePath)
    {
        var fileExtension = Path.GetExtension(filePath)?.ToLowerInvariant();
        if (FileExtensionsConsts.ModFileTypes.Contains(fileExtension))
        {
            return FileType.ModFile;
        }

        throw new NotSupportedException($"Unsupported file extension: {fileExtension}");
    }

    private async Task HandleModFileAsync(string filePath)
    {
        _logger.Info("Handling file: {FilePath}", filePath);

        try
        {
            var installed = await _modInstallService.InstallModAsync(filePath);
            if (installed)
            {
                _logger.Info("Successfully installed mod: {FilePath}", filePath);

                var fileName = Path.GetFileName(filePath);
                var taskId = Guid.NewGuid().ToString();

                var message =
                    WebSocketMessage.CreateStatus(taskId, "Installed File", $"Installed mod: {fileName}");

                _webSocketServer.BroadcastToEndpointAsync("/status", message)
                    .GetAwaiter()
                    .GetResult();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle file: {FilePath}", filePath);

            var taskId = Guid.NewGuid().ToString();
            var errorMessage =
                WebSocketMessage.CreateStatus(taskId, "Failed to handle file", $"Failed to handle file: {filePath}");

            _webSocketServer.BroadcastToEndpointAsync("/status", errorMessage)
                .GetAwaiter()
                .GetResult();
        }
    }
}