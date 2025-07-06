using System;
using System.Reflection;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.ViewModels;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.UI.Services
{
    public class UpdateCheckService : IUpdateCheckService
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
        private readonly UpdatePromptViewModel _updatePromptViewModel;
        private readonly string _currentVersion;

        public bool IsUpdateAvailable => _updatePromptViewModel.IsVisible;
        public string LatestVersion { get; private set; } = string.Empty;

        public UpdateCheckService(IUpdateService updateService, IRunUpdater runUpdater)
        {
            _updatePromptViewModel = new UpdatePromptViewModel(updateService, runUpdater);
            
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            _currentVersion = version == null ? "Local Build" : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        public async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                _logger.Debug("Checking for updates... Current version: {Version}", _currentVersion);
                
                await _updatePromptViewModel.CheckForUpdatesAsync(_currentVersion);
                
                _logger.Debug("Update check completed. IsUpdateAvailable: {IsAvailable}", IsUpdateAvailable);
                
                return IsUpdateAvailable;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check for updates");
                return false;
            }
        }
    }
}