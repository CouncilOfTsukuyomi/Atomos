using System;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Atomos.UI.Interfaces;
using Atomos.UI.ViewModels;
using Avalonia.Threading;
using CommonLib.Interfaces;
using NLog;
using ReactiveUI;

namespace Atomos.UI.Services;

public class UpdateManager : IUpdateManager, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IUpdateService _updateService;
    private readonly IRunUpdater _runUpdater;
    
    private Timer? _updateCheckTimer;
    private bool _isFirstBoot = true;
    private bool _isCheckingForUpdates;
    private bool _hasUpdateAvailable;
    private string _updateAvailableText = string.Empty;
    private bool _hasCriticalUpdates;
    private bool _hasExcessiveVersionLag;

    public UpdatePromptViewModel UpdatePromptViewModel { get; }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                _isCheckingForUpdates = value;
                IsCheckingForUpdatesChanged?.Invoke(value);
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _isCheckingForUpdates = value;
                    IsCheckingForUpdatesChanged?.Invoke(value);
                });
            }
        }
    }

    public bool HasUpdateAvailable
    {
        get => _hasUpdateAvailable;
        private set
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                _hasUpdateAvailable = value;
                HasUpdateAvailableChanged?.Invoke(value);
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _hasUpdateAvailable = value;
                    HasUpdateAvailableChanged?.Invoke(value);
                });
            }
        }
    }

    public string UpdateAvailableText
    {
        get => _updateAvailableText;
        private set
        {
            _updateAvailableText = value;
        }
    }

    public bool HasCriticalUpdates
    {
        get => _hasCriticalUpdates;
        private set
        {
            _hasCriticalUpdates = value;
        }
    }

    public bool HasExcessiveVersionLag
    {
        get => _hasExcessiveVersionLag;
        private set
        {
            _hasExcessiveVersionLag = value;
        }
    }

    public event Action<bool>? IsCheckingForUpdatesChanged;
    public event Action<bool>? HasUpdateAvailableChanged;
    public event Action? ShowUpdatePromptRequested;

    public UpdateManager(
        IUpdateCheckService updateCheckService, 
        IUpdateService updateService, 
        IRunUpdater runUpdater)
    {
        _updateCheckService = updateCheckService;
        _updateService = updateService;
        _runUpdater = runUpdater;
            
        UpdatePromptViewModel = new UpdatePromptViewModel(_updateService, _runUpdater);
        
        UpdatePromptViewModel.WhenAnyValue(x => x.IsVisible)
            .Subscribe(isVisible =>
            {
                if (!isVisible && HasUpdateAvailable)
                {
                    if (!_isFirstBoot)
                    {
                        _logger.Debug("Update prompt closed, but update still available. Keeping clickable indicator.");
                    }
                    else if (HasCriticalUpdates || HasExcessiveVersionLag)
                    {
                        _logger.Debug("Critical update or excessive version lag prompt dismissed on startup, but keeping indicator due to severity.");
                    }
                    else
                    {
                        HasUpdateAvailable = false;
                        _logger.Debug("Startup update prompt cancelled, hiding update indicator.");
                    }
                }
            });

        UpdatePromptViewModel.WhenAnyValue(x => x.HasCriticalUpdates)
            .Subscribe(hasCritical =>
            {
                HasCriticalUpdates = hasCritical;
                if (hasCritical)
                {
                    _logger.Warn("Critical updates detected, adjusting update behavior accordingly.");
                }
            });
        
        StartUpdateCheckTimer();
    }

    public async Task CheckForUpdatesAsync(string currentVersion)
    {
        try
        {
            _logger.Debug("CheckForUpdatesAsync started. IsFirstBoot: {IsFirstBoot}, UpdateCheckService.IsUpdateAvailable: {IsAvailable}", 
                _isFirstBoot, _updateCheckService.IsUpdateAvailable);
            
            if (!_updateCheckService.IsUpdateAvailable)
            {
                IsCheckingForUpdates = true;
                HasUpdateAvailable = false;
                    
                _logger.Debug("Checking for updates using UpdateCheckService...");
                
                var hasUpdate = await _updateCheckService.CheckForUpdatesAsync();
                
                _logger.Debug("Update check call completed. HasUpdate: {HasUpdate}", hasUpdate);
                    
                if (hasUpdate)
                {
                    _logger.Debug("Update detected, preparing update information...");
                    
                    await PrepareUpdateInformation(currentVersion);
                    
                    if (_isFirstBoot)
                    {
                        var isForced = HasCriticalUpdates || HasExcessiveVersionLag;
                        
                        _logger.Debug("First boot - showing update prompt immediately. Forced: {IsForced}, Critical: {HasCritical}, ExcessiveVersionLag: {HasExcessiveVersionLag}", 
                            isForced, HasCriticalUpdates, HasExcessiveVersionLag);
                        
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await UpdatePromptViewModel.CheckForUpdatesAsync(currentVersion, isForced: isForced);
                        });
                    }
                    else
                    {
                        if (HasCriticalUpdates || HasExcessiveVersionLag)
                        {
                            _logger.Debug("Scheduled check found critical updates or excessive version lag - showing update prompt immediately. Critical: {HasCritical}, ExcessiveVersionLag: {HasExcessiveVersionLag}", 
                                HasCriticalUpdates, HasExcessiveVersionLag);
                            await Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await UpdatePromptViewModel.CheckForUpdatesAsync(currentVersion, isForced: true);
                            });
                        }
                        else
                        {
                            _logger.Debug("Scheduled check - showing clickable update indicator");
                            HasUpdateAvailable = true;
                        }
                    }
                }
                else
                {
                    HasUpdateAvailable = false;
                    HasCriticalUpdates = false;
                    HasExcessiveVersionLag = false;
                }
            }
            else
            {
                _logger.Debug("Update already available");
                if (!HasUpdateAvailable)
                {
                    await PrepareUpdateInformation(currentVersion);
                    HasUpdateAvailable = true;
                }
                
                if (_isFirstBoot && !UpdatePromptViewModel.IsVisible)
                {
                    var isForced = HasCriticalUpdates || HasExcessiveVersionLag;
                    
                    _logger.Debug("First boot and update prompt not visible, showing update prompt. Forced: {IsForced}, Critical: {HasCritical}, ExcessiveVersionLag: {HasExcessiveVersionLag}", 
                        isForced, HasCriticalUpdates, HasExcessiveVersionLag);
                    
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await UpdatePromptViewModel.CheckForUpdatesAsync(currentVersion, isForced: isForced);
                    });
                }
            }

            if (_isFirstBoot)
            {
                _isFirstBoot = false;
                _logger.Info("First boot update check completed. Subsequent checks will be optional unless critical or excessive lag detected. Critical: {HasCritical}, ExcessiveVersionLag: {HasExcessiveVersionLag}", 
                    HasCriticalUpdates, HasExcessiveVersionLag);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for updates");
            HasUpdateAvailable = false;
            HasCriticalUpdates = false;
            HasExcessiveVersionLag = false;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task PrepareUpdateInformation(string currentVersion)
    {
        try
        {
            var allVersions = await _updateService.GetAllVersionInfoSinceCurrentAsync(currentVersion, "CouncilOfTsukuyomi/Atomos");
            
            if (allVersions?.Any() == true)
            {
                var latestVersion = allVersions.Last();
                var cleanedVersion = CleanVersionString(latestVersion.Version);
                
                HasCriticalUpdates = CheckForCriticalUpdates(allVersions);
                HasExcessiveVersionLag = CheckForExcessiveVersionLag(allVersions);
                
                if (HasCriticalUpdates)
                {
                    if (allVersions.Count > 1)
                    {
                        UpdateAvailableText = $"Critical Updates Required ({allVersions.Count})";
                        _logger.Debug("Prepared critical update text for multiple versions: {Text}", UpdateAvailableText);
                    }
                    else
                    {
                        UpdateAvailableText = $"Critical Update Required";
                        _logger.Debug("Prepared critical update text for single version: {Text}", UpdateAvailableText);
                    }
                }
                else if (HasExcessiveVersionLag)
                {
                    UpdateAvailableText = $"Forced Update Required ({allVersions.Count} versions behind)";
                    _logger.Debug("Prepared excessive version lag update text: {Text}", UpdateAvailableText);
                }
                else
                {
                    if (allVersions.Count > 1)
                    {
                        UpdateAvailableText = $"{allVersions.Count} Updates Available";
                        _logger.Debug("Prepared update text for multiple versions: {Text}", UpdateAvailableText);
                    }
                    else
                    {
                        UpdateAvailableText = $"Update to {cleanedVersion}";
                        _logger.Debug("Prepared update text for single version: {Text}", UpdateAvailableText);
                    }
                }
            }
            else
            {
                var latestVersion = await _updateService.GetMostRecentVersionAsync("CouncilOfTsukuyomi/Atomos");
                var cleanedVersion = CleanVersionString(latestVersion);
                HasCriticalUpdates = false;
                HasExcessiveVersionLag = false;
                UpdateAvailableText = $"Update to {cleanedVersion}";
                _logger.Debug("Prepared fallback update text: {Text}", UpdateAvailableText);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to prepare update information");
            HasCriticalUpdates = false;
            HasExcessiveVersionLag = false;
            UpdateAvailableText = "Update Available";
        }
    }

    private bool CheckForCriticalUpdates(System.Collections.Generic.List<CommonLib.Models.VersionInfo> versions)
    {
        try
        {
            foreach (var version in versions)
            {
                if (version.Changelog.Contains("[CRITICAL]", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warn("Critical update detected in version {Version} changelog", version.Version);
                    return true;
                }

                foreach (var change in version.Changes)
                {
                    if (change.Description.Contains("[CRITICAL]", StringComparison.OrdinalIgnoreCase) ||
                        change.OriginalText.Contains("[CRITICAL]", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warn("Critical update detected in version {Version} change: {Change}", 
                            version.Version, change.Description);
                        return true;
                    }
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error checking for critical updates");
            return false;
        }
    }

    private bool CheckForExcessiveVersionLag(System.Collections.Generic.List<CommonLib.Models.VersionInfo> versions)
    {
        try
        {
            if (versions.Count > 3)
            {
                _logger.Warn("Excessive version lag detected - user is {Count} versions behind, forcing update", versions.Count);
                return true;
            }
            
            _logger.Debug("Version lag check - user is {Count} versions behind, no forced update needed", versions.Count);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error checking for excessive version lag");
            return false;
        }
    }

    public void ShowUpdatePrompt()
    {
        _logger.Info("User clicked update indicator - showing update prompt. Critical: {HasCritical}, ExcessiveVersionLag: {HasExcessiveVersionLag}", 
            HasCriticalUpdates, HasExcessiveVersionLag);
        ShowUpdatePromptRequested?.Invoke();
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

    private void StartUpdateCheckTimer()
    {
        _logger.Debug("Starting update check timer (30 minute intervals)");

        _updateCheckTimer = new Timer(TimeSpan.FromMinutes(30).TotalMilliseconds);
        _updateCheckTimer.Elapsed += async (sender, e) =>
        {
            try
            {
                _logger.Debug("Timer elapsed - starting scheduled update check");
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                        var version = assembly.GetName().Version;
                        var currentVersion = version == null ? "Local Build" : $"{version.Major}.{version.Minor}.{version.Build}";
                        
                        await CheckForUpdatesAsync(currentVersion);
                        _logger.Debug("Scheduled update check completed. Critical: {HasCritical}, ExcessiveVersionLag: {HasExcessiveVersionLag}", 
                            HasCriticalUpdates, HasExcessiveVersionLag);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error during scheduled update check task");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during scheduled update check");
            }
        };
        _updateCheckTimer.AutoReset = true;
        _updateCheckTimer.Start();

        _logger.Debug("Update check timer started successfully");
    }

    public void Dispose()
    {
        try
        {
            if (_updateCheckTimer != null)
            {
                _updateCheckTimer.Stop();
                _updateCheckTimer.Dispose();
                _updateCheckTimer = null;
            }
            _logger.Debug("UpdateManager disposed - update check timer stopped");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error disposing UpdateManager");
        }
    }
}