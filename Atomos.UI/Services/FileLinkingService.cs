using System;
using System.IO;
using System.Reflection;
using Atomos.UI.Interfaces;
using CommonLib.Consts;
using NLog;

namespace Atomos.UI.Services;

public class FileLinkingService : IFileLinkingService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IRegistryHelper _registryHelper;

    private const string ConsoleToolingExe = "Atomos.ConsoleTooling.exe";
    private const string LauncherExe = "Atomos.Launcher.exe";
    private const string StartupAppName = "AtomosLauncher";

    private readonly string _consoleToolingPath;
    private readonly string _launcherPath;

    public FileLinkingService(IRegistryHelper registryHelper)
    {
        _registryHelper = registryHelper;

        // Determine the current directory (where this .dll or .exe is running from).
        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) 
                         ?? AppContext.BaseDirectory;

        // Build absolute paths to both exes.
        _consoleToolingPath = Path.Combine(currentDir, ConsoleToolingExe);
        _launcherPath = Path.Combine(currentDir, LauncherExe);
    }

    /// <summary>
    /// Enables file linking for the known allowed extensions, using ConsoleTooling to open them.
    /// </summary>
    public void EnableFileLinking()
    {
        if (!File.Exists(_consoleToolingPath))
        {
            _logger.Error("Console Tooling not found: {Path}", _consoleToolingPath);
            return;
        }

        // Register the .pmp, .ttmp, .ttmp2, .zip, .rar, .7z associations.
        _registryHelper.CreateFileAssociation(FileExtensionsConsts.AllowedExtensions, _consoleToolingPath);
    }

    /// <summary>
    /// Disables file linking for the known allowed extensions.
    /// </summary>
    public void DisableFileLinking()
    {
        _registryHelper.RemoveFileAssociation(FileExtensionsConsts.AllowedExtensions);
    }

    /// <summary>
    /// Adds the launcher to Windows startup (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
    /// </summary>
    public void EnableStartup()
    {
        if (!File.Exists(_launcherPath))
        {
            _logger.Error("Launcher not found: {Path}", _launcherPath);
            return;
        }

        _registryHelper.AddApplicationToStartup(StartupAppName, _launcherPath);
    }

    /// <summary>
    /// Removes the launcher from Windows startup.
    /// </summary>
    public void DisableStartup()
    {
        _registryHelper.RemoveApplicationFromStartup(StartupAppName);
    }
}