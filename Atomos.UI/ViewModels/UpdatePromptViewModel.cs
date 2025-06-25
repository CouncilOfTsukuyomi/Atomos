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

    // Convenience properties for UI binding
    public List<ChangeEntry> Changes => VersionInfo?.Changes ?? new List<ChangeEntry>();
    public bool HasChanges => Changes.Count > 0;
    public List<DownloadInfo> AvailableDownloads => VersionInfo?.AvailableDownloads ?? new List<DownloadInfo>();

    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
    public ReactiveCommand<string, Unit> OpenUrlCommand { get; }

    public UpdatePromptViewModel(IUpdateService updateService, IRunUpdater runUpdater)
    {
        _updateService = updateService;
        _runUpdater = runUpdater;

        var canExecuteUpdate = this.WhenAnyValue(x => x.IsUpdating, updating => !updating);
        UpdateCommand = ReactiveCommand.CreateFromTask(ExecuteUpdateCommand, canExecuteUpdate);
        
        // Command to open URLs (commits, PRs, etc.)
        OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);
    }

    private void OpenUrl(string url)
    {
        try
        {
            _logger.Info("Opening URL: {Url}", url);
            
            // Use the OS default browser to open the URL
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
                // Get the latest version info including changelog
                _logger.Debug("Fetching latest version information with changelog");
                var versionInfo = await _updateService.GetMostRecentVersionInfoAsync("CouncilOfTsukuyomi/Atomos");
                
                if (versionInfo != null)
                {
                    VersionInfo = versionInfo;
                    var cleanedVersion = CleanVersionString(versionInfo.Version);
                    TargetVersion = cleanedVersion;
                    
                    _logger.Debug("Latest version retrieved: {LatestVersion}, cleaned: {CleanedVersion}, changes: {ChangeCount}", 
                        versionInfo.Version, cleanedVersion, versionInfo.Changes.Count);

                    _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion} with {ChangeCount} changes", 
                        CurrentVersion, TargetVersion, versionInfo.Changes.Count);
                    
                    UpdateStatus = "Ready to update";
                    UpdateProgress = 0;
                    IsVisible = true;
                    
                    // Notify UI that changelog properties have changed
                    this.RaisePropertyChanged(nameof(Changes));
                    this.RaisePropertyChanged(nameof(HasChanges));
                    this.RaisePropertyChanged(nameof(AvailableDownloads));
                }
                else
                {
                    // Fallback to the old method if VersionInfo is not available
                    _logger.Debug("Falling back to GetMostRecentVersionAsync");
                    var latestVersion = await _updateService.GetMostRecentVersionAsync("CouncilOfTsukuyomi/Atomos");
                    var cleanedVersion = CleanVersionString(latestVersion);
                    TargetVersion = cleanedVersion;
                    
                    _logger.Debug("Latest version retrieved: {LatestVersion}, cleaned: {CleanedVersion}", latestVersion, cleanedVersion);
                    _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion}", CurrentVersion, TargetVersion);
                    
                    UpdateStatus = "Ready to update";
                    UpdateProgress = 0;
                    IsVisible = true;
                }
            }
            else
            {
                _logger.Debug("No updates available for version: {CurrentVersion}", CurrentVersion);
                if (IsVisible)
                {
                    IsVisible = false;
                    VersionInfo = null;
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
                
            UpdateStatus = "Initializing update process...";
            _logger.Info("User initiated update process from {CurrentVersion} to {TargetVersion}", CurrentVersion, TargetVersion);
            await Task.Delay(500);
                
            UpdateStatus = "Preparing update environment...";
            var currentExePath = Environment.ProcessPath ?? 
                                 Process.GetCurrentProcess().MainModule?.FileName ?? 
                                 Assembly.GetExecutingAssembly().Location;
            var installPath = Path.GetDirectoryName(currentExePath) ?? AppContext.BaseDirectory;
            var programToRunAfterInstallation = "Atomos.Launcher.exe";
            await Task.Delay(500);

            // Create progress reporter for the update process
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