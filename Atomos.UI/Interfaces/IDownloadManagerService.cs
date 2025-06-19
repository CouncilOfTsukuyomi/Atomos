using System.Threading;
using System.Threading.Tasks;
using CommonLib.Models;

namespace Atomos.UI.Interfaces;

public interface IDownloadManagerService
{
    Task DownloadModsAsync(XmaMods mod, CancellationToken ct);
}