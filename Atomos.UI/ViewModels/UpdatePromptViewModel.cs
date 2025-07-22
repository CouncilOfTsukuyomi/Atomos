using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using CommonLib.Interfaces;
using CommonLib.Models;
using CommonLib.Services;
using NLog;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class UpdatePromptViewModel : ViewModelBase
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IUpdateService _updateService;
    private readonly IRunUpdater _runUpdater;

    private bool _isVisible;
    private string _currentVersion = string.Empty;
    private string _targetVersion = string.Empty;
    private bool _isUpdating;
    private string _updateStatus = "Ready to update";
    private double _updateProgress;
    private VersionInfo? _versionInfo;
    private List<VersionInfo> _allVersions = new();
    private string _consolidatedChangelog = string.Empty;
    private bool _showAllVersions;
    private bool _isForced;
    private bool _canCancel = true;
    private bool _hasCriticalUpdates;
    private string _cancelDisabledMessage = string.Empty;
    private string _updateRequiredReason = string.Empty;
    private List<string> _criticalReasons = new();
    private bool _hasSecurityWarning = false;
    private string _securityWarningMessage = string.Empty;
    private bool _updateBlocked = false;

    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    public string CurrentVersion
    {
        get => _currentVersion;
        set => this.RaiseAndSetIfChanged(ref _currentVersion, value);
    }

    public string TargetVersion
    {
        get => _targetVersion;
        set => this.RaiseAndSetIfChanged(ref _targetVersion, value);
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        set => this.RaiseAndSetIfChanged(ref _isUpdating, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
    }

    public double UpdateProgress
    {
        get => _updateProgress;
        set => this.RaiseAndSetIfChanged(ref _updateProgress, value);
    }

    public VersionInfo? VersionInfo
    {
        get => _versionInfo;
        set => this.RaiseAndSetIfChanged(ref _versionInfo, value);
    }

    public List<VersionInfo> AllVersions
    {
        get => _allVersions;
        set => this.RaiseAndSetIfChanged(ref _allVersions, value);
    }

    public string ConsolidatedChangelog
    {
        get => _consolidatedChangelog;
        set => this.RaiseAndSetIfChanged(ref _consolidatedChangelog, value);
    }

    public bool ShowAllVersions
    {
        get => _showAllVersions;
        set => this.RaiseAndSetIfChanged(ref _showAllVersions, value);
    }

    public bool IsForced
    {
        get => _isForced;
        set
        {
            this.RaiseAndSetIfChanged(ref _isForced, value);
            UpdateCancellationState();
        }
    }

    public bool CanCancel
    {
        get => _canCancel;
        set => this.RaiseAndSetIfChanged(ref _canCancel, value);
    }

    public bool HasCriticalUpdates
    {
        get => _hasCriticalUpdates;
        set
        {
            this.RaiseAndSetIfChanged(ref _hasCriticalUpdates, value);
            UpdateCancellationState();
        }
    }

    public string CancelDisabledMessage
    {
        get => _cancelDisabledMessage;
        set => this.RaiseAndSetIfChanged(ref _cancelDisabledMessage, value);
    }

    public string UpdateRequiredReason
    {
        get => _updateRequiredReason;
        set => this.RaiseAndSetIfChanged(ref _updateRequiredReason, value);
    }

    public List<string> CriticalReasons
    {
        get => _criticalReasons;
        set => this.RaiseAndSetIfChanged(ref _criticalReasons, value);
    }

    public bool HasSecurityWarning
    {
        get => _hasSecurityWarning;
        set
        {
            this.RaiseAndSetIfChanged(ref _hasSecurityWarning, value);
            UpdateCancellationState();
        }
    }

    public string SecurityWarningMessage
    {
        get => _securityWarningMessage;
        set => this.RaiseAndSetIfChanged(ref _securityWarningMessage, value);
    }

    public bool UpdateBlocked
    {
        get => _updateBlocked;
        set => this.RaiseAndSetIfChanged(ref _updateBlocked, value);
    }

    public bool HasUpdateRequiredReason => !string.IsNullOrEmpty(UpdateRequiredReason);
    public bool HasCriticalReasons => CriticalReasons.Count > 0;
    public bool HasSecurityError => !string.IsNullOrEmpty(SecurityWarningMessage);
    
    public List<ChangeEntry> Changes => VersionInfo?.Changes ?? new List<ChangeEntry>();
    public bool HasChanges => Changes.Count > 0;
    public List<DownloadInfo> AvailableDownloads => VersionInfo?.AvailableDownloads ?? new List<DownloadInfo>();
    
    public bool HasMultipleVersions => AllVersions.Count > 1;
    public int VersionCount => AllVersions.Count;
    
    public string UpdateSubtitle => HasSecurityWarning
        ? "SECURITY ALERT: Update source has been compromised"
        : IsForced && HasUpdateRequiredReason
            ? UpdateRequiredReason
            : HasCriticalUpdates && HasCriticalReasons
                ? $"Critical update required: {string.Join(", ", CriticalReasons.Take(2))}{(CriticalReasons.Count > 2 ? "..." : "")}"
                : HasMultipleVersions 
                    ? $"{VersionCount} versions of Atomos are ready to install"
                    : "A new version of Atomos is ready to install";
        
    public string UpdateButtonText => HasSecurityWarning 
        ? "Update Blocked"
        : IsForced 
            ? "Update Required" 
            : HasMultipleVersions 
                ? $"Install {VersionCount} Updates"
                : "Update Now";
        
    public string VersionCountText => HasMultipleVersions 
        ? $"Updating through {VersionCount} versions"
        : string.Empty;

    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
    public ReactiveCommand<string, Unit> OpenUrlCommand { get; }
    public ReactiveCommand<ChangeEntry, Unit> OpenAuthorProfileCommand { get; }
    public ReactiveCommand<ChangeEntry, Unit> OpenCommitCommand { get; }
    public ReactiveCommand<ChangeEntry, Unit> OpenPullRequestCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleVersionViewCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public UpdatePromptViewModel(IUpdateService updateService, IRunUpdater runUpdater)
    {
        _updateService = updateService;
        _runUpdater = runUpdater;

        var canExecuteUpdate = this.WhenAnyValue(
            x => x.IsUpdating, 
            x => x.UpdateBlocked,
            (updating, blocked) => !updating && !blocked);
        UpdateCommand = ReactiveCommand.CreateFromTask(ExecuteUpdateCommand, canExecuteUpdate);
        
        OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);
        OpenAuthorProfileCommand = ReactiveCommand.Create<ChangeEntry>(OpenAuthorProfile);
        OpenCommitCommand = ReactiveCommand.Create<ChangeEntry>(OpenCommit);
        OpenPullRequestCommand = ReactiveCommand.Create<ChangeEntry>(OpenPullRequest);
        ToggleVersionViewCommand = ReactiveCommand.Create(ToggleVersionView);
        
        CancelCommand = ReactiveCommand.Create(
            () => { IsVisible = false; },
            this.WhenAnyValue(x => x.CanCancel)
        );
    }

    private void ToggleVersionView()
    {
        ShowAllVersions = !ShowAllVersions;
    }

    private void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        
        try
        {
            _logger.Info("Opening URL: {Url}", url);
            
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open URL: {Url}", url);
        }
    }

    private void OpenAuthorProfile(ChangeEntry changeEntry)
    {
        if (!changeEntry.HasAuthor) return;
        
        var url = changeEntry.AuthorUrl;
        _logger.Info("Opening author profile: {Author} -> {Url}", changeEntry.Author, url);
        OpenUrl(url);
    }

    private void OpenCommit(ChangeEntry changeEntry)
    {
        if (!changeEntry.HasCommitHash) return;
        
        var url = changeEntry.CommitUrl;
        _logger.Info("Opening commit: {CommitHash} -> {Url}", changeEntry.CommitHash, url);
        OpenUrl(url);
    }

    private void OpenPullRequest(ChangeEntry changeEntry)
    {
        if (!changeEntry.HasPullRequest) return;
        
        var url = changeEntry.PullRequestUrl;
        _logger.Info("Opening pull request: #{PrNumber} -> {Url}", changeEntry.PullRequestNumber, url);
        OpenUrl(url);
    }

    private bool CheckForCriticalUpdates(List<VersionInfo> versions)
    {
        var criticalReasons = new List<string>();
        var hasCritical = false;

        foreach (var version in versions)
        {
            if (version.Changelog.Contains("[CRITICAL]", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn("Critical update detected in version {Version} changelog", version.Version);
                hasCritical = true;
                
                ExtractCriticalReasons(version.Changelog, criticalReasons);
            }

            foreach (var change in version.Changes)
            {
                if (change.Description.Contains("[CRITICAL]", StringComparison.OrdinalIgnoreCase) ||
                    change.OriginalText.Contains("[CRITICAL]", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warn("Critical update detected in version {Version} change: {Change}", 
                        version.Version, change.Description);
                    hasCritical = true;
                    
                    var reason = ExtractCriticalReasonFromChange(change);
                    if (!string.IsNullOrEmpty(reason) && !criticalReasons.Contains(reason))
                    {
                        criticalReasons.Add(reason);
                    }
                }
            }
        }

        CriticalReasons = criticalReasons.Distinct().ToList();
        return hasCritical;
    }

    private void ExtractCriticalReasons(string changelog, List<string> reasons)
    {
        try
        {
            var lines = changelog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("[CRITICAL]", StringComparison.OrdinalIgnoreCase))
                {
                    var reason = ExtractReasonFromLine(line);
                    if (!string.IsNullOrEmpty(reason) && !reasons.Contains(reason))
                    {
                        reasons.Add(reason);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error extracting critical reasons from changelog");
        }
    }

    private string ExtractCriticalReasonFromChange(ChangeEntry change)
    {
        try
        {
            var text = change.Description;
            if (string.IsNullOrEmpty(text))
                text = change.OriginalText;

            return ExtractReasonFromLine(text);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error extracting critical reason from change");
            return string.Empty;
        }
    }

    private string ExtractReasonFromLine(string line)
    {
        try
        {
            var criticalIndex = line.IndexOf("[CRITICAL", StringComparison.OrdinalIgnoreCase);
            if (criticalIndex >= 0)
            {
                var endBracket = line.IndexOf(']', criticalIndex);
                if (endBracket >= 0)
                {
                    var afterMarker = line.Substring(endBracket + 1).Trim();
                    
                    if (afterMarker.StartsWith(":"))
                        afterMarker = afterMarker.Substring(1).Trim();
                    if (afterMarker.StartsWith("-"))
                        afterMarker = afterMarker.Substring(1).Trim();

                    var sentences = afterMarker.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (sentences.Length > 0)
                    {
                        var reason = sentences[0].Trim();
                        if (reason.Length > 100)
                            reason = reason.Substring(0, 97) + "...";
                        
                        return reason;
                    }
                }
            }
            
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error parsing critical reason from line: {Line}", line);
            return string.Empty;
        }
    }

    private void UpdateCancellationState()
    {
        if (HasSecurityWarning)
        {
            CanCancel = true;
            UpdateBlocked = true;
            UpdateRequiredReason = "Security validation failed - update blocked for your protection";
            CancelDisabledMessage = string.Empty;
            _logger.Error("Update blocked due to security warning: {SecurityWarning}", SecurityWarningMessage);
        }
        else if (IsForced)
        {
            CanCancel = false;
            UpdateBlocked = false;
            UpdateRequiredReason = DetermineUpdateRequiredReason();
            CancelDisabledMessage = "This update is required and cannot be cancelled.";
            _logger.Info("Update cancellation disabled: Forced update - Reason: {Reason}", UpdateRequiredReason);
        }
        else if (HasCriticalUpdates)
        {
            CanCancel = false;
            UpdateBlocked = false;
            UpdateRequiredReason = "Critical security or stability updates detected";
            CancelDisabledMessage = "Critical security or stability updates detected. Update cannot be cancelled.";
            _logger.Info("Update cancellation disabled: Critical updates detected - Reasons: {Reasons}", 
                string.Join(", ", CriticalReasons));
        }
        else
        {
            CanCancel = true;
            UpdateBlocked = false;
            UpdateRequiredReason = string.Empty;
            CancelDisabledMessage = string.Empty;
            _logger.Debug("Update cancellation allowed");
        }
        
        this.RaisePropertyChanged(nameof(HasUpdateRequiredReason));
        this.RaisePropertyChanged(nameof(HasCriticalReasons));
        this.RaisePropertyChanged(nameof(HasSecurityError));
        this.RaisePropertyChanged(nameof(UpdateSubtitle));
        this.RaisePropertyChanged(nameof(UpdateButtonText));
    }

    private string DetermineUpdateRequiredReason()
    {
        if (HasCriticalUpdates && CriticalReasons.Count > 0)
        {
            return $"Critical update required: {CriticalReasons.First()}";
        }
        
        if (HasMultipleVersions && VersionCount > 3)
        {
            return $"You are {VersionCount} versions behind - update required for compatibility";
        }
        
        return "This update is required to continue using the application";
    }

    private void HandleSecurityViolation(UpdateService.SecurityException secEx)
    {
        _logger.Error("SECURITY ALERT: {Message}", secEx.Message);
        
        HasSecurityWarning = true;
        UpdateBlocked = true;
        IsUpdating = false;
        
        SecurityWarningMessage = $"Security Warning: Update blocked for your safety.\n\n" +
                                $"The release '{secEx.ReleaseTag}' from repository '{secEx.Repository}' " +
                                $"was created by '{secEx.AuthorLogin}', which is not a trusted author.\n\n" +
                                $"This update has been blocked to protect your system from potentially malicious releases.";
        
        UpdateStatus = "Update Blocked - Security Violation Detected";
        
        this.RaisePropertyChanged(nameof(HasSecurityWarning));
        this.RaisePropertyChanged(nameof(UpdateBlocked));
        this.RaisePropertyChanged(nameof(SecurityWarningMessage));
        this.RaisePropertyChanged(nameof(HasSecurityError));
        this.RaisePropertyChanged(nameof(UpdateSubtitle));
        this.RaisePropertyChanged(nameof(UpdateButtonText));
    }

    private void ClearSecurityWarning()
    {
        HasSecurityWarning = false;
        UpdateBlocked = false;
        SecurityWarningMessage = string.Empty;
        
        this.RaisePropertyChanged(nameof(HasSecurityWarning));
        this.RaisePropertyChanged(nameof(UpdateBlocked));
        this.RaisePropertyChanged(nameof(SecurityWarningMessage));
        this.RaisePropertyChanged(nameof(HasSecurityError));
        this.RaisePropertyChanged(nameof(UpdateSubtitle));
        this.RaisePropertyChanged(nameof(UpdateButtonText));
    }

    public async Task CheckForUpdatesAsync(string currentVersion, bool isForced = false)
    {
        _logger.Debug("UpdatePromptViewModel.CheckForUpdatesAsync called with version: {CurrentVersion}, isForced: {IsForced}", 
            currentVersion, isForced);
    
        try
        {
            _logger.Debug("Starting update check for version: {CurrentVersion}", currentVersion);
            IsForced = isForced;
            CurrentVersion = currentVersion;
            
            ClearSecurityWarning();

            _logger.Debug("About to call _updateService.NeedsUpdateAsync");
            var isUpdateNeeded = await _updateService.NeedsUpdateAsync(currentVersion, "CouncilOfTsukuyomi/Atomos");
            _logger.Debug("_updateService.NeedsUpdateAsync returned: {IsUpdateNeeded}", isUpdateNeeded);

            if (isUpdateNeeded)
            {
                _logger.Debug("Fetching all version information since current version");
                var allVersionsSinceCurrentValue = await _updateService.GetAllVersionInfoSinceCurrentAsync(currentVersion, "CouncilOfTsukuyomi/Atomos");
                
                if (allVersionsSinceCurrentValue?.Any() == true)
                {
                    AllVersions = allVersionsSinceCurrentValue;
                    
                    HasCriticalUpdates = CheckForCriticalUpdates(allVersionsSinceCurrentValue);
                    
                    var latestVersion = allVersionsSinceCurrentValue.Last();
                    VersionInfo = latestVersion;
                    
                    var cleanedVersion = CleanVersionString(latestVersion.Version);
                    TargetVersion = cleanedVersion;
                    
                    _logger.Debug("Fetching consolidated changelog");
                    var consolidatedChangelogValue = await _updateService.GetConsolidatedChangelogSinceCurrentAsync(currentVersion, "CouncilOfTsukuyomi/Atomos");
                    ConsolidatedChangelog = consolidatedChangelogValue;
                    
                    var totalChanges = allVersionsSinceCurrentValue.Sum(v => v.Changes.Count);
                    
                    _logger.Debug("Retrieved {VersionCount} versions with {TotalChanges} total changes. Latest version: {LatestVersion}. Critical updates: {HasCritical}", 
                        allVersionsSinceCurrentValue.Count, totalChanges, cleanedVersion, HasCriticalUpdates);

                    _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion} ({VersionCount} versions, {TotalChanges} total changes, Critical: {HasCritical}, Forced: {IsForced})", 
                        CurrentVersion, TargetVersion, allVersionsSinceCurrentValue.Count, totalChanges, HasCriticalUpdates, IsForced);
                    
                    UpdateStatus = HasMultipleVersions 
                        ? $"Ready to update ({VersionCount} versions)" 
                        : "Ready to update";
                    UpdateProgress = 0;
                    ShowAllVersions = false;
                    IsVisible = true;
                    
                    this.RaisePropertyChanged(nameof(Changes));
                    this.RaisePropertyChanged(nameof(HasChanges));
                    this.RaisePropertyChanged(nameof(AvailableDownloads));
                    this.RaisePropertyChanged(nameof(HasMultipleVersions));
                    this.RaisePropertyChanged(nameof(VersionCount));
                    this.RaisePropertyChanged(nameof(UpdateSubtitle));
                    this.RaisePropertyChanged(nameof(UpdateButtonText));
                    this.RaisePropertyChanged(nameof(VersionCountText));
                    this.RaisePropertyChanged(nameof(HasUpdateRequiredReason));
                    this.RaisePropertyChanged(nameof(HasCriticalReasons));
                    this.RaisePropertyChanged(nameof(CriticalReasons));
                    this.RaisePropertyChanged(nameof(UpdateRequiredReason));
                }
                else
                {
                    _logger.Debug("Falling back to single version fetch");
                    var versionInfo = await _updateService.GetMostRecentVersionInfoAsync("CouncilOfTsukuyomi/Atomos");
                    
                    if (versionInfo != null)
                    {
                        VersionInfo = versionInfo;
                        AllVersions = new List<VersionInfo> { versionInfo };
                        
                        HasCriticalUpdates = CheckForCriticalUpdates(new List<VersionInfo> { versionInfo });
                        
                        var cleanedVersion = CleanVersionString(versionInfo.Version);
                        TargetVersion = cleanedVersion;
                        ConsolidatedChangelog = versionInfo.Changelog;
                        
                        _logger.Debug("Fallback: Latest version retrieved: {LatestVersion}, cleaned: {CleanedVersion}, changes: {ChangeCount}, Critical: {HasCritical}", 
                            versionInfo.Version, cleanedVersion, versionInfo.Changes.Count, HasCriticalUpdates);

                        _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion} with {ChangeCount} changes (Critical: {HasCritical}, Forced: {IsForced})", 
                            CurrentVersion, TargetVersion, versionInfo.Changes.Count, HasCriticalUpdates, IsForced);
                        
                        UpdateStatus = "Ready to update";
                        UpdateProgress = 0;
                        ShowAllVersions = false;
                        IsVisible = true;
                        
                        this.RaisePropertyChanged(nameof(Changes));
                        this.RaisePropertyChanged(nameof(HasChanges));
                        this.RaisePropertyChanged(nameof(AvailableDownloads));
                        this.RaisePropertyChanged(nameof(HasMultipleVersions));
                        this.RaisePropertyChanged(nameof(VersionCount));
                        this.RaisePropertyChanged(nameof(UpdateSubtitle));
                        this.RaisePropertyChanged(nameof(UpdateButtonText));
                        this.RaisePropertyChanged(nameof(VersionCountText));
                        this.RaisePropertyChanged(nameof(HasUpdateRequiredReason));
                        this.RaisePropertyChanged(nameof(HasCriticalReasons));
                        this.RaisePropertyChanged(nameof(CriticalReasons));
                        this.RaisePropertyChanged(nameof(UpdateRequiredReason));
                    }
                    else
                    {
                        _logger.Debug("Final fallback to GetMostRecentVersionAsync");
                        var latestVersion = await _updateService.GetMostRecentVersionAsync("CouncilOfTsukuyomi/Atomos");
                        var cleanedVersion = CleanVersionString(latestVersion);
                        TargetVersion = cleanedVersion;
                        
                        AllVersions = new List<VersionInfo>();
                        VersionInfo = null;
                        ConsolidatedChangelog = string.Empty;
                        ShowAllVersions = false;
                        HasCriticalUpdates = false;
                        
                        _logger.Debug("Final fallback: Latest version retrieved: {LatestVersion}, cleaned: {CleanedVersion}, Forced: {IsForced}", 
                            latestVersion, cleanedVersion, IsForced);
                        _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion} (Forced: {IsForced})", 
                            CurrentVersion, TargetVersion, IsForced);
                        
                        UpdateStatus = "Ready to update";
                        UpdateProgress = 0;
                        IsVisible = true;
                        
                        this.RaisePropertyChanged(nameof(UpdateSubtitle));
                        this.RaisePropertyChanged(nameof(UpdateButtonText));
                        this.RaisePropertyChanged(nameof(VersionCountText));
                        this.RaisePropertyChanged(nameof(HasUpdateRequiredReason));
                        this.RaisePropertyChanged(nameof(HasCriticalReasons));
                        this.RaisePropertyChanged(nameof(CriticalReasons));
                        this.RaisePropertyChanged(nameof(UpdateRequiredReason));
                    }
                }
            }
            else
            {
                _logger.Debug("No updates available for version: {CurrentVersion}", CurrentVersion);
                if (IsVisible)
                {
                    IsVisible = false;
                    VersionInfo = null;
                    AllVersions = new List<VersionInfo>();
                    ConsolidatedChangelog = string.Empty;
                    ShowAllVersions = false;
                    HasCriticalUpdates = false;
                    CanCancel = true;
                    CancelDisabledMessage = string.Empty;
                    UpdateRequiredReason = string.Empty;
                    CriticalReasons = new List<string>();
                    ClearSecurityWarning();
                    
                    this.RaisePropertyChanged(nameof(HasMultipleVersions));
                    this.RaisePropertyChanged(nameof(VersionCount));
                    this.RaisePropertyChanged(nameof(UpdateSubtitle));
                    this.RaisePropertyChanged(nameof(UpdateButtonText));
                    this.RaisePropertyChanged(nameof(VersionCountText));
                    this.RaisePropertyChanged(nameof(HasUpdateRequiredReason));
                    this.RaisePropertyChanged(nameof(HasCriticalReasons));
                    this.RaisePropertyChanged(nameof(CriticalReasons));
                    this.RaisePropertyChanged(nameof(UpdateRequiredReason));
                }
            }
        }
        catch (UpdateService.SecurityException secEx)
        {
            _logger.Error(secEx, "SECURITY VIOLATION during update check - {SecurityError}", secEx.Message);
            HandleSecurityViolation(secEx);
            
            IsVisible = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for updates");
        }
    
        _logger.Debug("UpdatePromptViewModel.CheckForUpdatesAsync completed");
    }

    private async Task ExecuteUpdateCommand()
    {
        if (UpdateBlocked)
        {
            _logger.Warn("Update command blocked due to security warning");
            return;
        }

        try
        {
            ClearSecurityWarning();
            
            IsUpdating = true;
            UpdateProgress = 0;
                
            var versionText = HasMultipleVersions ? $"{VersionCount} versions" : TargetVersion;
            var updateType = IsForced ? "Required update" : "Update";
            var criticalNote = HasCriticalUpdates ? " (includes critical updates)" : "";
            UpdateStatus = $"Initializing {updateType.ToLower()} process for {versionText}{criticalNote}...";
            
            _logger.Info("User initiated update process from {CurrentVersion} to {TargetVersion} ({VersionCount} versions, Forced: {IsForced}, Critical: {HasCritical})", 
                CurrentVersion, TargetVersion, VersionCount, IsForced, HasCriticalUpdates);
            await Task.Delay(500);
                
            UpdateStatus = "Preparing update environment...";
            var currentExePath = Environment.ProcessPath ?? 
                                 Process.GetCurrentProcess().MainModule?.FileName ?? 
                                 Assembly.GetExecutingAssembly().Location;
            var installPath = Path.GetDirectoryName(currentExePath) ?? AppContext.BaseDirectory;
            var programToRunAfterInstallation = "Atomos.Launcher.exe";
            await Task.Delay(500);
            
            var progress = new Progress<DownloadProgress>(OnUpdateProgressChanged);
            
            _logger.Debug("Starting download and update process");
            
            var updateResult = await _runUpdater.RunDownloadedUpdaterAsync(
                CurrentVersion,
                "CouncilOfTsukuyomi/Atomos",
                installPath,
                true,
                programToRunAfterInstallation,
                progress);

            if (updateResult)
            {
                UpdateStatus = "Update completed! Restarting application...";
                UpdateProgress = 100;
                _logger.Info("Update to {TargetVersion} completed successfully. Restarting application.", TargetVersion);
                    
                await Task.Delay(2000);
                    
                _logger.Info("Shutting down for update restart");
                LogManager.Shutdown();
                Environment.Exit(0);
            }
            else
            {
                UpdateStatus = "Update failed. Please try again later.";
                UpdateProgress = 0;
                _logger.Warn("Update to {TargetVersion} failed or updater was not detected running", TargetVersion);
                IsUpdating = false;
                    
                await Task.Delay(4000);
                if (!IsForced && !HasCriticalUpdates)
                {
                    IsVisible = false;
                }
            }
        }
        catch (UpdateService.SecurityException secEx)
        {
            _logger.Error(secEx, "SECURITY VIOLATION during update execution - {SecurityError}", secEx.Message);
            HandleSecurityViolation(secEx);
            IsUpdating = false;
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update failed due to an error.";
            UpdateProgress = 0;
            _logger.Error(ex, "Error during update process to {TargetVersion}", TargetVersion);
            IsUpdating = false;
                
            await Task.Delay(4000);
            if (!IsForced && !HasCriticalUpdates)
            {
                IsVisible = false;
            }
        }
    }

    private void OnUpdateProgressChanged(DownloadProgress progress)
    {
        _logger.Debug("=== UPDATE PROGRESS UI UPDATE ===");
        _logger.Debug("Status: {Status}", progress.Status);
        _logger.Debug("Progress: {Progress}%", progress.PercentComplete);
        
        UpdateStatus = progress.Status ?? "Updating...";
        UpdateProgress = progress.PercentComplete;

        _logger.Debug("Updated UI - Status: {Status}, Progress: {Progress}%", UpdateStatus, UpdateProgress);
        _logger.Debug("=== END UPDATE PROGRESS UI UPDATE ===");
    }
        
    private static string CleanVersionString(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return version;
            
        var cleaned = version.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(1);
        }

        return cleaned;
    }
}