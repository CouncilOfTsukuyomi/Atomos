using System.Diagnostics;

namespace Atomos.Watchdog.Interfaces;

public interface IProcessManager : IDisposable
{
    public void Run();
    public void Dispose();
}