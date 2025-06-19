namespace Atomos.BackgroundWorker.Interfaces;

public interface IModHandlerService
{
    Task HandleFileAsync(string filePath);
}