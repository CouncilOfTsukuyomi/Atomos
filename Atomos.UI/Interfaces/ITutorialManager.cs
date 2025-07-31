using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Atomos.UI.Models;
using Atomos.UI.ViewModels;

namespace Atomos.UI.Interfaces;
    
public interface ITutorialManager : IDisposable
{
    TutorialOverlayViewModel TutorialViewModel { get; }
    event Action<string>? NavigationRequested;

    void Initialize(
        Func<ObservableCollection<MenuItem>> getMenuItems,
        Action<MenuItem> setSelectedMenuItem,
        Action<ViewModelBase> setCurrentPage,
        Func<ViewModelBase> navigateToSettings);

    Task CheckFirstRunAsync();
    void StartFirstRunTutorial();
    void StartFeatureTutorial(string featureName);
    void StartPluginsTutorial();
}