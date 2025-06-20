using System.Threading;
using System.Threading.Tasks;
using PluginManager.Core.Models;

namespace Atomos.UI.Interfaces;

public interface IDownloadManagerService
{
    Task DownloadModAsync(PluginMod pluginMod, CancellationToken ct = default);
}
