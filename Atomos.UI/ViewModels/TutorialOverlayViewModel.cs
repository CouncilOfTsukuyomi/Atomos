using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Atomos.UI.Enums.Tutorial;
using Atomos.UI.Interfaces.Tutorial;
using Atomos.UI.Models.Tutorial;
using Atomos.UI.Services.Tutorial;
using Avalonia;
using ReactiveUI;
using NLog;

namespace Atomos.UI.ViewModels;

public class TutorialOverlayViewModel : ReactiveObject, IActivatableViewModel
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly ITutorialService _tutorialService;
    private readonly IElementHighlightService _highlightService;
    private readonly ObservableAsPropertyHelper<TutorialStep?> _currentStep;
    private readonly ObservableAsPropertyHelper<bool> _isVisible;
    private readonly ObservableAsPropertyHelper<bool> _canGoPrevious;
    private readonly ObservableAsPropertyHelper<bool> _canGoNext;
    private readonly ObservableAsPropertyHelper<string> _nextButtonText;
    private readonly ObservableAsPropertyHelper<string> _progressText;
    private readonly ObservableAsPropertyHelper<bool> _isRequiredStep;
    private readonly ObservableAsPropertyHelper<Avalonia.Layout.HorizontalAlignment> _horizontalAlignment;
    private readonly ObservableAsPropertyHelper<Avalonia.Layout.VerticalAlignment> _verticalAlignment;
    private readonly ObservableAsPropertyHelper<Thickness> _margin;
    private readonly Subject<Unit> _refreshTrigger = new();

    public ViewModelActivator Activator { get; }

    public event Action<string>? NavigationRequested;

    public TutorialOverlayViewModel(ITutorialService tutorialService, IElementHighlightService highlightService)
    {
        _tutorialService = tutorialService;
        _highlightService = highlightService;
        Activator = new ViewModelActivator();
        
        _tutorialService.CanProceedChanged += () =>
        {
            _logger.Debug("TutorialService.CanProceedChanged event received, triggering refresh");
            RefreshCanProceed();
        };
            
        NextCommand = ReactiveCommand.Create(
            () => {
                _logger.Debug("Next button clicked - Current step: {StepId}", CurrentStep?.Id);
                HandleStepCompletion();
                _tutorialService.NextStep();
            },
            this.WhenAnyValue(x => x.CanGoNext));

        PreviousCommand = ReactiveCommand.Create(
            () => {
                _logger.Debug("Previous button clicked - Current step: {StepId}", CurrentStep?.Id);
                _tutorialService.PreviousStep();
            },
            this.WhenAnyValue(x => x.CanGoPrevious));

        SkipCommand = ReactiveCommand.Create(
            () => {
                _logger.Info("Tutorial skipped by user");
                _highlightService.RemoveHighlight();
                _tutorialService.CancelTutorial();
            });
            
        _currentStep = _tutorialService.StepChanged
            .Do(step => {
                _logger.Debug("Tutorial step changed to: {StepId} - {Title}", step?.Id, step?.Title);
                if (step != null)
                {
                    _highlightService.HighlightElement(step.TargetElementName);
                }
            })
            .ToProperty(this, x => x.CurrentStep, scheduler: RxApp.MainThreadScheduler);

        _isVisible = _tutorialService.StepChanged
            .Select(_ => true)
            .Merge(_tutorialService.TutorialCompleted.Select(_ => false))
            .Merge(_tutorialService.TutorialCancelled.Select(_ => false))
            .Do(visible => {
                _logger.Debug("Tutorial visibility changed to: {IsVisible}", visible);
                if (!visible)
                {
                    _highlightService.RemoveHighlight();
                }
            })
            .ToProperty(this, x => x.IsVisible, scheduler: RxApp.MainThreadScheduler);

        _canGoPrevious = this.WhenAnyValue(x => x.CurrentStep)
            .Select(step => 
            {
                try
                {
                    return step != null && _tutorialService.CurrentStepIndex > 0;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error evaluating CanGoPrevious, returning false");
                    return false;
                }
            })
            .ToProperty(this, x => x.CanGoPrevious, scheduler: RxApp.MainThreadScheduler);
        
        _canGoNext = Observable.CombineLatest(
            this.WhenAnyValue(x => x.CurrentStep),
            _refreshTrigger.StartWith(Unit.Default),
            (step, _) => new { Step = step })
            .ObserveOn(RxApp.MainThreadScheduler)
            .Select(x => 
            {
                try 
                {
                    var step = x.Step;
                    if (step == null) 
                    {
                        _logger.Debug("CanGoNext: No current step, returning false");
                        return false;
                    }
                        
                    if (!step.IsRequired)
                    {
                        _logger.Debug("CanGoNext: Step {StepId} is not required, returning true", step.Id);
                        return true;
                    }
                        
                    var canProceed = _tutorialService?.CanProceedToNext() ?? true;
                    _logger.Debug("CanGoNext: Step {StepId} is required, CanProceed: {CanProceed}", step.Id, canProceed);
                    return canProceed;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error evaluating CanProceedToNext, returning false");
                    return false;
                }
            })
            .DistinctUntilChanged()
            .Do(canProceed => _logger.Debug("CanGoNext value changed to: {CanProceed}", canProceed))
            .ToProperty(this, x => x.CanGoNext, scheduler: RxApp.MainThreadScheduler);

        _nextButtonText = this.WhenAnyValue(x => x.CurrentStep)
            .Select(step => 
            {
                try
                {
                    return step != null && _tutorialService.CurrentStepIndex == _tutorialService.TotalSteps - 1 
                        ? "Finish" : "Next";
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error evaluating NextButtonText, returning 'Next'");
                    return "Next";
                }
            })
            .ToProperty(this, x => x.NextButtonText, scheduler: RxApp.MainThreadScheduler);

        _progressText = this.WhenAnyValue(x => x.CurrentStep)
            .Select(step => 
            {
                try
                {
                    return step != null 
                        ? $"Step {_tutorialService.CurrentStepIndex + 1} of {_tutorialService.TotalSteps}"
                        : string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error evaluating ProgressText, returning empty string");
                    return string.Empty;
                }
            })
            .ToProperty(this, x => x.ProgressText, scheduler: RxApp.MainThreadScheduler);

        _isRequiredStep = this.WhenAnyValue(x => x.CurrentStep)
            .Select(step => step?.IsRequired ?? false)
            .ToProperty(this, x => x.IsRequiredStep, scheduler: RxApp.MainThreadScheduler);
            
        _horizontalAlignment = this.WhenAnyValue(x => x.CurrentStep)
            .Select(step => GetHorizontalAlignment(step))
            .ToProperty(this, x => x.HorizontalAlignment, scheduler: RxApp.MainThreadScheduler);

        _verticalAlignment = this.WhenAnyValue(x => x.CurrentStep)
            .Select(step => GetVerticalAlignment(step))
            .ToProperty(this, x => x.VerticalAlignment, scheduler: RxApp.MainThreadScheduler);

        _margin = this.WhenAnyValue(x => x.CurrentStep)
            .Select(step => GetMargin(step))
            .ToProperty(this, x => x.Margin, scheduler: RxApp.MainThreadScheduler);

        _tutorialService.TutorialCompleted.Subscribe(_ => 
            _logger.Info("Tutorial completed successfully"));
                
        _tutorialService.TutorialCancelled.Subscribe(_ => 
            _logger.Info("Tutorial was cancelled"));
    }

    private Avalonia.Layout.HorizontalAlignment GetHorizontalAlignment(TutorialStep? step)
    {
        if (step == null) return Avalonia.Layout.HorizontalAlignment.Center;

        var elementBounds = _highlightService.GetElementBounds(step.TargetElementName);
        if (elementBounds.HasValue)
        {
            return step.Position switch
            {
                TutorialPosition.Left => Avalonia.Layout.HorizontalAlignment.Left,
                TutorialPosition.Right => Avalonia.Layout.HorizontalAlignment.Right,
                TutorialPosition.Center => Avalonia.Layout.HorizontalAlignment.Center,
                _ => elementBounds.Value.X < 400 ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right
            };
        }

        return step.Position switch
        {
            TutorialPosition.Left => Avalonia.Layout.HorizontalAlignment.Left,
            TutorialPosition.Right => Avalonia.Layout.HorizontalAlignment.Right,
            TutorialPosition.Center => Avalonia.Layout.HorizontalAlignment.Center,
            _ => Avalonia.Layout.HorizontalAlignment.Center
        };
    }

    private Avalonia.Layout.VerticalAlignment GetVerticalAlignment(TutorialStep? step)
    {
        if (step == null) return Avalonia.Layout.VerticalAlignment.Center;

        var elementBounds = _highlightService.GetElementBounds(step.TargetElementName);
        if (elementBounds.HasValue)
        {
            return step.Position switch
            {
                TutorialPosition.Top => Avalonia.Layout.VerticalAlignment.Top,
                TutorialPosition.Bottom => Avalonia.Layout.VerticalAlignment.Bottom,
                TutorialPosition.Center => Avalonia.Layout.VerticalAlignment.Center,
                _ => elementBounds.Value.Y < 300 ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom
            };
        }

        return step.Position switch
        {
            TutorialPosition.Top => Avalonia.Layout.VerticalAlignment.Top,
            TutorialPosition.Bottom => Avalonia.Layout.VerticalAlignment.Bottom,
            TutorialPosition.Center => Avalonia.Layout.VerticalAlignment.Center,
            _ => Avalonia.Layout.VerticalAlignment.Center
        };
    }

    private Thickness GetMargin(TutorialStep? step)
    {
        if (step == null) return new Thickness(20);

        var elementBounds = _highlightService.GetElementBounds(step.TargetElementName);
        if (elementBounds.HasValue)
        {
            var bounds = elementBounds.Value;
            return step.Position switch
            {
                TutorialPosition.Left => new Thickness(20, bounds.Y, 0, 0),
                TutorialPosition.Right => new Thickness(0, bounds.Y, 20, 0),
                TutorialPosition.Top => new Thickness(bounds.X, 20, 0, 0),
                TutorialPosition.Bottom => new Thickness(bounds.X, 0, 0, 20),
                TutorialPosition.Center => new Thickness(0),
                _ => new Thickness(20)
            };
        }

        return step.Position switch
        {
            TutorialPosition.Left => new Thickness(20, 0, 0, 0),
            TutorialPosition.Right => new Thickness(0, 0, 20, 0),
            TutorialPosition.Top => new Thickness(0, 20, 0, 0),
            TutorialPosition.Bottom => new Thickness(0, 0, 0, 20),
            TutorialPosition.Center => new Thickness(0),
            _ => new Thickness(20)
        };
    }

    public TutorialStep? CurrentStep => _currentStep?.Value;
    public bool IsVisible => _isVisible?.Value ?? false;
    public bool CanGoPrevious => _canGoPrevious?.Value ?? false;
    public bool CanGoNext => _canGoNext?.Value ?? false;
    public string NextButtonText => _nextButtonText?.Value ?? "Next";
    public string ProgressText => _progressText?.Value ?? string.Empty;
    public bool IsRequiredStep => _isRequiredStep?.Value ?? false;
    public Avalonia.Layout.HorizontalAlignment HorizontalAlignment => _horizontalAlignment?.Value ?? Avalonia.Layout.HorizontalAlignment.Center;
    public Avalonia.Layout.VerticalAlignment VerticalAlignment => _verticalAlignment?.Value ?? Avalonia.Layout.VerticalAlignment.Center;
    public Thickness Margin => _margin?.Value ?? new Thickness(20);

    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipCommand { get; }

    private void HandleStepCompletion()
    {
        var currentStep = CurrentStep;
        if (currentStep == null) return;

        try
        {
            currentStep.OnStepCompleted?.Invoke();
            
            switch (currentStep.Id)
            {
                case "welcome":
                    _logger.Debug("Welcome step completed, requesting navigation to Settings");
                    NavigationRequested?.Invoke("settings");
                    break;
                case "navigation":
                    _logger.Debug("Navigation step completed, requesting navigation to Home");
                    NavigationRequested?.Invoke("home");
                    break;
                case "download-path":
                case "file-linking":
                case "auto-start":
                    if (currentStep.TargetElementName != null)
                    {
                        TriggerTabNavigation(currentStep.TargetElementName);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling step completion for step: {StepId}", currentStep.Id);
        }
    }

    public event Action<string>? TabNavigationRequested;

    private void TriggerTabNavigation(string elementName)
    {
        TabNavigationRequested?.Invoke(elementName);
    }

    public void RefreshCanProceed()
    {
        _logger.Debug("RefreshCanProceed called, triggering refresh");
        _refreshTrigger.OnNext(Unit.Default);
    }
}