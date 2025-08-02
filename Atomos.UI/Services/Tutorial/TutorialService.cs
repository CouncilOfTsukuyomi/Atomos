using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.Interfaces.Tutorial;
using Atomos.UI.Models.Tutorial;
using ReactiveUI;
using NLog;
using CommonLib.Interfaces;

namespace Atomos.UI.Services.Tutorial;

public class TutorialService : ReactiveObject, ITutorialService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly List<TutorialStep> _steps = new();
    private readonly Subject<TutorialStep> _stepChanged = new();
    private readonly Subject<Unit> _tutorialCompleted = new();
    private readonly Subject<Unit> _tutorialCancelled = new();
    private readonly IConfigurationService _configurationService;
    private readonly CompositeDisposable _disposables = new();
    
    private int _currentStepIndex = -1;
    private bool _isFirstRun = false;

    public IObservable<TutorialStep> StepChanged => _stepChanged.AsObservable();
    public IObservable<Unit> TutorialCompleted => _tutorialCompleted.AsObservable();
    public IObservable<Unit> TutorialCancelled => _tutorialCancelled.AsObservable();

    public TutorialStep? CurrentStep => _currentStepIndex >= 0 && _currentStepIndex < _steps.Count 
        ? _steps[_currentStepIndex] : null;

    public bool IsActive => _currentStepIndex >= 0;
    public bool IsFirstRun => _isFirstRun;
    public int CurrentStepIndex => _currentStepIndex;
    public int TotalSteps => _steps.Count;

    public event Func<string, Task>? NavigationRequested;
    public event Action<string>? TabNavigationRequested;
    public event Action? CanProceedChanged;

    public TutorialService(IConfigurationService configurationService, IConfigurationChangeStream configChangeStream)
    {
        _configurationService = configurationService;
        
        configChangeStream.ConfigurationChanges
            .Where(_ => CurrentStep?.CanProceed != null)
            .Where(e => IsRelevantConfigurationChange(e.PropertyName))
            .Do(e => _logger.Debug("Configuration changed for property: {PropertyName}, re-evaluating CanProceed for step: {StepId}", 
                e.PropertyName, CurrentStep?.Id))
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async _ => 
            {
                _logger.Debug("Debounced configuration change - waiting for pending operations to complete");
                
                try
                {
                    var maxWaitTime = TimeSpan.FromSeconds(3);
                    var startTime = DateTime.UtcNow;
                    
                    while (_configurationService.GetPendingOperationCount() > 0 && 
                           DateTime.UtcNow - startTime < maxWaitTime)
                    {
                        await Task.Delay(50);
                    }
                    
                    var waitTime = DateTime.UtcNow - startTime;
                    _logger.Debug("Waited {WaitTime:F1}ms for configuration operations to complete. Pending: {PendingCount}", 
                        waitTime.TotalMilliseconds, _configurationService.GetPendingOperationCount());
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error waiting for configuration operations to complete");
                }
                
                _logger.Debug("Updating UI state after configuration change");
                CanProceedChanged?.Invoke();
                this.RaisePropertyChanged(nameof(CanProceedToNext));
            })
            .DisposeWith(_disposables);
    }

    private bool IsRelevantConfigurationChange(string propertyName)
    {
        return CurrentStep?.Id switch
        {
            "download-path" => propertyName.Contains("DownloadPath"),
            "file-linking" => propertyName.Contains("FileLinking") || propertyName.Contains("DoubleClick"),
            "auto-start" => propertyName.Contains("StartOnBoot") || propertyName.Contains("AutoStart"),
            _ => false
        };
    }

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
            switch (step.Id)
            {
                case "welcome":
                    break;
                case "download-path":
                case "file-linking":
                case "auto-start":
                case "settings-search":
                    if (NavigationRequested != null)
                    {
                        await NavigationRequested("settings");
                        await Task.Delay(100);
                    }
                    break;
                case "navigation":
                case "home-overview":
                    if (NavigationRequested != null)
                    {
                        await NavigationRequested("home");
                        await Task.Delay(100);
                    }
                    break;
                case "plugin-view-mods":
                    if (NavigationRequested != null)
                    {
                        await NavigationRequested("Plugin Data");
                        await Task.Delay(100);
                    }
                    break;
            }

            if (step.TargetElementName != null && IsSettingsElement(step.TargetElementName))
            {
                _logger.Debug("Requesting tab navigation for element: {ElementName}", step.TargetElementName);
                TabNavigationRequested?.Invoke(step.TargetElementName);
                await Task.Delay(200);
            }

            _stepChanged.OnNext(step);
            
            await Task.Delay(100);
            this.RaisePropertyChanged(nameof(CanProceedToNext));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling navigation for step: {StepId}", step.Id);
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
        var canProceed = CurrentStep?.CanProceed?.Invoke() ?? true;
        _logger.Debug("CanProceedToNext evaluated for step {StepId}: {CanProceed}", CurrentStep?.Id, canProceed);
        return canProceed;
    }

    public void Dispose()
    {
        _disposables?.Dispose();
        _stepChanged?.Dispose();
        _tutorialCompleted?.Dispose();
        _tutorialCancelled?.Dispose();
    }
}