using System;
using System.Threading.Tasks;
using Atomos.UI.ViewModels;

namespace Atomos.UI.Interfaces;

public interface ISentryManager : IDisposable
{
    SentryPromptViewModel SentryPromptViewModel { get; }
    event Func<Task>? SentryChoiceMade;
}