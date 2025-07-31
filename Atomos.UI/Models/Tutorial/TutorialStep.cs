using System;
using Atomos.UI.Enums.Tutorial;
using ReactiveUI;

namespace Atomos.UI.Models.Tutorial;

public class TutorialStep : ReactiveObject
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetElementName { get; set; } = string.Empty;
    public TutorialPosition Position { get; set; } = TutorialPosition.Bottom;
    public bool IsRequired { get; set; } = false;
    public Func<bool>? CanProceed { get; set; }
    public Action? OnStepCompleted { get; set; }
}
