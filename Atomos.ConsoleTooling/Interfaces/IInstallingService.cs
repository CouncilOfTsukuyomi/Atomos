namespace Atomos.ConsoleTooling.Interfaces;

public interface IInstallingService
{
    Task HandleFileAsync(string filePath);
}