using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Atomos.UI.Interfaces.Tutorial;
using Atomos.UI.Models.Tutorial;
using ReactiveUI;
using NLog;

namespace Atomos.UI.Services.Tutorial;

public class TutorialService : ReactiveObject, ITutorialService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly List<TutorialStep> _steps = new();
    private readonly Subject<TutorialStep> _stepChanged = new();
    private readonly Subject<Unit> _tutorialCompleted = new();
    private readonly Subject<Unit> _tutorialCancelled = new();
    
    private int _currentStepIndex = -1;
    private bool _isFirstRun = false;

    public IObservable<TutorialStep> StepChanged => _stepChanged;
    public IObservable<Unit> TutorialCompleted => _tutorialCompleted;
    public IObservable<Unit> TutorialCancelled => _tutorialCancelled;

    public TutorialStep? CurrentStep => _currentStepIndex >= 0 && _currentStepIndex < _steps.Count 
        ? _steps[_currentStepIndex] : null;

    public bool IsActive => _currentStepIndex >= 0;
    public bool IsFirstRun => _isFirstRun;
    public int CurrentStepIndex => _currentStepIndex;
    public int TotalSteps => _steps.Count;

    public event Func<string, Task>? NavigationRequested;
    public event Action<string>? TabNavigationRequested;

    public void StartTutorial(List<TutorialStep> steps, bool isFirstRun = false)
    {
        _steps.Clear();
        _steps.AddRange(steps);
        _currentStepIndex = 0;
        _isFirstRun = isFirstRun;
        
        if (_steps.Count > 0)
        {
            _ = HandleStepNavigation(_steps[0]);
        }
    }

    public void NextStep()
    {
        if (_currentStepIndex < _steps.Count - 1)
        {
            CurrentStep?.OnStepCompleted?.Invoke();
            _currentStepIndex++;
            _ = HandleStepNavigation(_steps[_currentStepIndex]);
        }
        else
        {
            CompleteTutorial();
        }
    }

    public void PreviousStep()
    {
        if (_currentStepIndex > 0)
        {
            _currentStepIndex--;
            _ = HandleStepNavigation(_steps[_currentStepIndex]);
        }
    }

    private async Task HandleStepNavigation(TutorialStep step)
    {
        _logger.Debug("Handling navigation for step: {StepId} targeting {TargetElement}", step.Id, step.TargetElementName);
        
        try
        {
            // Handle page navigation first
            switch (step.Id)
            {
                case "welcome":
                    // Welcome step - navigate to settings after this step
                    break;
                case "download-path":
                case "file-linking":
                case "auto-start":
                case "settings-search":
                    // These are settings steps - make sure we're on settings page
                    if (NavigationRequested != null)
                    {
                        await NavigationRequested("settings");
                        // Small delay to ensure navigation completes
                        await Task.Delay(100);
                    }
                    break;
                case "navigation":
                case "home-overview":
                    // These are for home page
                    if (NavigationRequested != null)
                    {
                        await NavigationRequested("home");
                        await Task.Delay(100);
                    }
                    break;
            }

            // Handle tab navigation for settings elements
            if (step.TargetElementName != null && IsSettingsElement(step.TargetElementName))
            {
                _logger.Debug("Requesting tab navigation for element: {ElementName}", step.TargetElementName);
                TabNavigationRequested?.Invoke(step.TargetElementName);
                // Small delay to ensure tab switch completes
                await Task.Delay(200);
            }

            // Now emit the step change
            _stepChanged.OnNext(step);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling navigation for step: {StepId}", step.Id);
            // Still emit the step change even if navigation fails
            _stepChanged.OnNext(step);
        }
    }

    private bool IsSettingsElement(string elementName)
    {
        return elementName switch
        {
            "DownloadPathSetting" or
            "FileLinkingEnabled" or
            "StartOnBoot" or
            "EnableSentry" or
            "EnableDebugLogs" => true,
            _ => false
        };
    }

    public void CompleteTutorial()
    {
        _currentStepIndex = -1;
        _steps.Clear();
        _tutorialCompleted.OnNext(Unit.Default);
    }

    public void CancelTutorial()
    {
        _currentStepIndex = -1;
        _steps.Clear();
        _tutorialCancelled.OnNext(Unit.Default);
    }

    public bool CanProceedToNext()
    {
        return CurrentStep?.CanProceed?.Invoke() ?? true;
    }
}