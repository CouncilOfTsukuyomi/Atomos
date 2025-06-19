namespace Atomos.Watchdog.Interfaces;

public interface IRunUpdater
{
    Task<bool> RunDownloadedUpdaterAsync(
        string versionNumber,
        string gitHubRepo,
        string installationPath,
        bool enableSentry,
        string? programToRunAfterInstallation = null);
}