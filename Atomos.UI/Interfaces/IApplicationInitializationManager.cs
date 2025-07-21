using System;
using System.Threading.Tasks;

namespace Atomos.UI.Interfaces;
    
public interface IApplicationInitializationManager : IDisposable
{
    Task InitializeAsync(int port, Func<Task> checkForUpdatesAsync, Func<Task> checkFirstRunAsync);
}