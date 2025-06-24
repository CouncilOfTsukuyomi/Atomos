
using System;
using System.Diagnostics;
using System.IO;
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

    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }

    public UpdatePromptViewModel(IUpdateService updateService, IRunUpdater runUpdater)
    {
        _updateService = updateService;
        _runUpdater = runUpdater;

        var canExecuteUpdate = this.WhenAnyValue(x => x.IsUpdating, updating => !updating);
        UpdateCommand = ReactiveCommand.CreateFromTask(ExecuteUpdateCommand, canExecuteUpdate);
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
                // Get the latest version to display
                _logger.Debug("Fetching latest version information");
                var latestVersion = await _updateService.GetMostRecentVersionAsync("CouncilOfTsukuyomi/Atomos");
                    
                var cleanedVersion = CleanVersionString(latestVersion);
                TargetVersion = cleanedVersion;
                    
                _logger.Debug("Latest version retrieved: {LatestVersion}, cleaned: {CleanedVersion}", latestVersion, cleanedVersion);

                _logger.Info("Update available for version: {CurrentVersion} -> {TargetVersion}", CurrentVersion, TargetVersion);
                UpdateStatus = "Ready to update";
                UpdateProgress = 0;
                IsVisible = true;
            }
            else
            {
                _logger.Debug("No updates available for version: {CurrentVersion}", CurrentVersion);
                if (IsVisible)
                {
                    IsVisible = false;
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
                progress); // Pass the progress reporter

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