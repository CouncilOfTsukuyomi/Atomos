using System;
using CommonLib.Events;

namespace Atomos.UI.Interfaces;

public interface IConfigurationChangeStream
{
    IObservable<ConfigurationChangedEventArgs> ConfigurationChanges { get; }
}
