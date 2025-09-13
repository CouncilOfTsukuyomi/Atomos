using Atomos.ConsoleTooling.Interfaces;
using CommonLib.Consts;
using CommonLib.Enums;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.ConsoleTooling.Services;

public class InstallingService : IInstallingService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IModInstallService _modInstallService;
    private readonly ISoundManagerService _soundManagerService;
    private readonly ITexToolsHelper _texToolsHelper;

    public InstallingService(
        IModInstallService modInstallService,
        ISoundManagerService soundManagerService, ITexToolsHelper texToolsHelper)
    {
        _modInstallService = modInstallService;
        _soundManagerService = soundManagerService;
        _texToolsHelper = texToolsHelper;
    }
        
    private async Task CheckTexToolsInstallation()
    {
        var status = _texToolsHelper.SetTexToolConsolePath();

        switch (status)
        {
            case TexToolsStatus.NotInstalled:
                await _soundManagerService.PlaySoundAsync(SoundType.GeneralChime);
                _logger.Warn("TexTools installation not found");
                break;
            case TexToolsStatus.NotFound:
                await _soundManagerService.PlaySoundAsync(SoundType.GeneralChime);
                _logger.Warn("TexTools executable not found");
                break;
        }
    }

    public async Task HandleFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or whitespace.", nameof(filePath));
            
        // Before we even think about doing anything with files we need to check for textools
        await CheckTexToolsInstallation();
            

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
            // If the mod is installed successfully, play a sound and log.
            if (await _modInstallService.InstallModAsync(filePath))
            {
                await _soundManagerService.PlaySoundAsync(SoundType.GeneralChime);
                _logger.Info("Successfully installed mod: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle file: {FilePath}", filePath);
        }
    }
}