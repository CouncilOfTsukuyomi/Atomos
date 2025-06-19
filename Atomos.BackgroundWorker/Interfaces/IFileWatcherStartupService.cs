namespace Atomos.BackgroundWorker.Interfaces;

public interface IFileWatcherService : IDisposable
{
    Task Start();
    void Stop();
}