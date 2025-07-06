using System.Threading.Tasks;

namespace Atomos.UI.Interfaces
{
    public interface IUpdateCheckService
    {
        Task<bool> CheckForUpdatesAsync();
        bool IsUpdateAvailable { get; }
        string LatestVersion { get; }
    }
}