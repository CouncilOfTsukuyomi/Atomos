using System;
using System.Threading;
using System.Threading.Tasks;
using CommonLib.Models;
using PluginManager.Core.Models;

namespace Atomos.UI.Interfaces;

public interface IDownloadManagerService
{
    Task DownloadModAsync(PluginMod pluginMod, CancellationToken ct = default, IProgress<DownloadProgress>? progress = null);
}