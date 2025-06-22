using System.Threading.Tasks;

namespace Atomos.UI.Interfaces;

public interface IRunUpdater
{
    Task<bool> RunDownloadedUpdaterAsync(
        string versionNumber,
        string gitHubRepo,
        string installationPath,
        bool enableSentry,
        string? programToRunAfterInstallation = null);
}