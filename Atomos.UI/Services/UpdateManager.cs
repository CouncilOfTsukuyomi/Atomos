using System;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.ViewModels;
using Avalonia.Threading;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.UI.Services;

public class UpdateManager : IUpdateManager
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IUpdateService _updateService;
    private readonly IRunUpdater _runUpdater;
        
    private bool _isCheckingForUpdates;

    public UpdatePromptViewModel UpdatePromptViewModel { get; }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            _isCheckingForUpdates = value;
            IsCheckingForUpdatesChanged?.Invoke(value);
        }
    }

    public event Action<bool>? IsCheckingForUpdatesChanged;

    public UpdateManager(
        IUpdateCheckService updateCheckService, 
        IUpdateService updateService, 
        IRunUpdater runUpdater)
    {
        _updateCheckService = updateCheckService;
        _updateService = updateService;
        _runUpdater = runUpdater;
            
        UpdatePromptViewModel = new UpdatePromptViewModel(_updateService, _runUpdater);
    }

    public async Task CheckForUpdatesAsync(string currentVersion)
    {
        try
        {
            _logger.Debug("CheckForUpdatesAsync started. UpdateCheckService.IsUpdateAvailable: {IsAvailable}", _updateCheckService.IsUpdateAvailable);
            
            if (!_updateCheckService.IsUpdateAvailable)
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsCheckingForUpdates = true);
                    
                _logger.Debug("Checking for updates using UpdateCheckService...");
                
                var hasUpdate = await _updateCheckService.CheckForUpdatesAsync();
                
                _logger.Debug("Update check call completed. HasUpdate: {HasUpdate}", hasUpdate);
                    
                if (hasUpdate)
                {
                    _logger.Debug("Update detected, triggering UpdatePromptViewModel.CheckForUpdatesAsync");
                    await UpdatePromptViewModel.CheckForUpdatesAsync(currentVersion);
                }
            }
            else
            {
                _logger.Debug("Skipping update check - update is already available");
                if (!UpdatePromptViewModel.IsVisible)
                {
                    _logger.Debug("Update is available but prompt not visible, triggering UpdatePromptViewModel.CheckForUpdatesAsync");
                    await UpdatePromptViewModel.CheckForUpdatesAsync(currentVersion);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for updates");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsCheckingForUpdates = false);
        }
    }
}