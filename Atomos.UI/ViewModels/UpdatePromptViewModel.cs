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
    
    public List<ChangeEntry> Changes => VersionInfo?.Changes ?? new List<ChangeEntry>();
    public bool HasChanges => Changes.Count > 0;
    public List<DownloadInfo> AvailableDownloads => VersionInfo?.AvailableDownloads ?? new List<DownloadInfo>();
    
    public bool HasMultipleVersions => AllVersions.Count > 1;
    public int VersionCount => AllVersions.Count;
    
    public string UpdateSubtitle => HasMultipleVersions 
        ? $"{VersionCount} versions of Atomos are ready to install"
        : "A new version of Atomos is ready to install";
        
    public string UpdateButtonText => HasMultipleVersions 
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

    public UpdatePromptViewModel(IUpdateService updateService, IRunUpdater runUpdater)
    {
        _updateService = updateService;
        _runUpdater = runUpdater;

        var canExecuteUpdate = this.WhenAnyValue(x => x.IsUpdating, updating => !updating);
        UpdateCommand = ReactiveCommand.CreateFromTask(ExecuteUpdateCommand, canExecuteUpdate);
        
        OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);
        OpenAuthorProfileCommand = ReactiveCommand.Create<ChangeEntry>(OpenAuthorProfile);
        OpenCommitCommand = ReactiveCommand.Create<ChangeEntry>(OpenCommit);
        OpenPullRequestCommand = ReactiveCommand.Create<ChangeEntry>(OpenPullRequest);
        ToggleVersionViewCommand = ReactiveCommand.Create(ToggleVersionView);
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

    public async Task CheckForUpdatesAsync(string currentVersion)
    {
        _logger.Debug("UpdatePromptViewModel.CheckForUpdatesAsync called with version: {CurrentVersion}", currentVersion);
    
        try
        {
            _logger.Debug("Starting update check for version: {CurrentVersion}", currentVersion);
            CurrentVersion = currentVersion;

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
                    
                    var latestVersion = allVersionsSinceCurrentValue.Last();
                    VersionInfo = latestVersion;
                    
                    var cleanedVersion = CleanVersionString(latestVersion.Version);
                    TargetVersion = cleanedVersion;
                    
                    _logger.Debug("Fetching consolidated changelog");
                    var consolidatedChangelogValue = await _updateService.GetConsolidatedChangelogSinceCurrentAsync(currentVersion, "CouncilOfTsukuyomi/Atomos");
                    ConsolidatedChangelog = consolidatedChangelogValue;
                    
                    var totalChanges = allVersionsSinceCurrentValue.Sum(v => v.Changes.Count);
                    
                    _logger.Debug("Retrieved {VersionCount} versions with {TotalChanges} total changes. Latest version: {LatestVersion}", 
                        allVersionsSinceCurrentValue.Count, totalChanges, cleanedVersion);

                    _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion} ({VersionCount} versions, {TotalChanges} total changes)", 
                        CurrentVersion, TargetVersion, allVersionsSinceCurrentValue.Count, totalChanges);
                    
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
                }
                else
                {
                    _logger.Debug("Falling back to single version fetch");
                    var versionInfo = await _updateService.GetMostRecentVersionInfoAsync("CouncilOfTsukuyomi/Atomos");
                    
                    if (versionInfo != null)
                    {
                        VersionInfo = versionInfo;
                        AllVersions = new List<VersionInfo> { versionInfo };
                        var cleanedVersion = CleanVersionString(versionInfo.Version);
                        TargetVersion = cleanedVersion;
                        ConsolidatedChangelog = versionInfo.Changelog;
                        
                        _logger.Debug("Fallback: Latest version retrieved: {LatestVersion}, cleaned: {CleanedVersion}, changes: {ChangeCount}", 
                            versionInfo.Version, cleanedVersion, versionInfo.Changes.Count);

                        _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion} with {ChangeCount} changes", 
                            CurrentVersion, TargetVersion, versionInfo.Changes.Count);
                        
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
                        
                        _logger.Debug("Final fallback: Latest version retrieved: {LatestVersion}, cleaned: {CleanedVersion}", latestVersion, cleanedVersion);
                        _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion}", CurrentVersion, TargetVersion);
                        
                        UpdateStatus = "Ready to update";
                        UpdateProgress = 0;
                        IsVisible = true;
                        
                        this.RaisePropertyChanged(nameof(UpdateSubtitle));
                        this.RaisePropertyChanged(nameof(UpdateButtonText));
                        this.RaisePropertyChanged(nameof(VersionCountText));
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
                    
                    this.RaisePropertyChanged(nameof(HasMultipleVersions));
                    this.RaisePropertyChanged(nameof(VersionCount));
                    this.RaisePropertyChanged(nameof(UpdateSubtitle));
                    this.RaisePropertyChanged(nameof(UpdateButtonText));
                    this.RaisePropertyChanged(nameof(VersionCountText));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for updates");
        }
    
        _logger.Debug("UpdatePromptViewModel.CheckForUpdatesAsync completed");
    }

    private async Task ExecuteUpdateCommand()
    {
        try
        {
            IsUpdating = true;
            UpdateProgress = 0;
                
            var versionText = HasMultipleVersions ? $"{VersionCount} versions" : TargetVersion;
            UpdateStatus = $"Initializing update process for {versionText}...";
            _logger.Info("User initiated update process from {CurrentVersion} to {TargetVersion} ({VersionCount} versions)", 
                CurrentVersion, TargetVersion, VersionCount);
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
                IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update failed due to an error.";
            UpdateProgress = 0;
            _logger.Error(ex, "Error during update process to {TargetVersion}", TargetVersion);
            IsUpdating = false;
                
            await Task.Delay(4000);
            IsVisible = false;
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