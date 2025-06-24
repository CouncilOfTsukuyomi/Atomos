using System;
using System.Threading.Tasks;
using CommonLib.Models;

namespace Atomos.UI.Interfaces;

public interface IRunUpdater
{
    Task<bool> RunDownloadedUpdaterAsync(
        string versionNumber,
        string gitHubRepo,
        string installationPath,
        bool enableSentry,
        string? programToRunAfterInstallation = null,
        IProgress<DownloadProgress>? progress = null);
}