using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using Atomos.UI.Models.Tutorial;

namespace Atomos.UI.Interfaces.Tutorial;

public interface ITutorialService
{
    IObservable<TutorialStep> StepChanged { get; }
    IObservable<Unit> TutorialCompleted { get; }
    IObservable<Unit> TutorialCancelled { get; }
    
    TutorialStep? CurrentStep { get; }
    bool IsActive { get; }
    bool IsFirstRun { get; }
    int CurrentStepIndex { get; }
    int TotalSteps { get; }
    
    event Func<string, Task>? NavigationRequested;
    event Action<string>? TabNavigationRequested;
    
    void StartTutorial(List<TutorialStep> steps, bool isFirstRun = false);
    void NextStep();
    void PreviousStep();
    void CompleteTutorial();
    void CancelTutorial();
    bool CanProceedToNext();
}