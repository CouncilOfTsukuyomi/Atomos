using System;
using CommonLib.Events;

namespace Atomos.UI.Interfaces;

public interface IConfigurationListener
{
    event EventHandler<ConfigurationChangedEventArgs>? TutorialRelevantConfigurationChanged;
}