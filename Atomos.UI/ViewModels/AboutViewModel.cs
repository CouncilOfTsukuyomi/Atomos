using System;
using System.Reflection;
using System.Windows.Input;
using ReactiveUI;
using System.Diagnostics;
using System.IO;

namespace Atomos.UI.ViewModels
{
    public class AboutViewModel : ViewModelBase
    {
        private string _version = string.Empty;
        private string _applicationName = string.Empty;
        private string _description = string.Empty;
        private string _buildDate = string.Empty;

        public string Version
        {
            get => _version;
            set => this.RaiseAndSetIfChanged(ref _version, value);
        }

        public string ApplicationName
        {
            get => _applicationName;
            set => this.RaiseAndSetIfChanged(ref _applicationName, value);
        }

        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        public string BuildDate
        {
            get => _buildDate;
            set => this.RaiseAndSetIfChanged(ref _buildDate, value);
        }

        public ICommand OpenDiscordCommand { get; }
        public ICommand OpenGitHubCommand { get; }
        public ICommand OpenLicenseCommand { get; }
        public ICommand OpenSevenZipExtractorCommand { get; }
        public ICommand OpenAvaloniaCommand { get; }
        public ICommand OpenReactiveUICommand { get; }

        public AboutViewModel()
        {
            // Initialize with application info
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            Version = version == null ? "Local Build" : $"v{version.Major}.{version.Minor}.{version.Build}";
            ApplicationName = "Atomos Mod Forwarder";
            Description = "A modern tool for managing and installing mods for Final Fantasy XIV with Penumbra and Textools integration.";
            
            // Get build date from assembly
            var buildDate = GetBuildDate(assembly);
            BuildDate = buildDate.ToString("MMMM dd, yyyy");

            // Initialize commands
            OpenDiscordCommand = ReactiveCommand.Create(OpenDiscord);
            OpenGitHubCommand = ReactiveCommand.Create(OpenGitHub);
            OpenLicenseCommand = ReactiveCommand.Create(OpenLicense);
            OpenSevenZipExtractorCommand = ReactiveCommand.Create(OpenSevenZipExtractor);
            OpenAvaloniaCommand = ReactiveCommand.Create(OpenAvalonia);
            OpenReactiveUICommand = ReactiveCommand.Create(OpenReactiveUI);
        }

        private DateTime GetBuildDate(Assembly assembly)
        {
            try
            {
                // Try to get the build date from the assembly file write time
                var location = assembly.Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    return File.GetLastWriteTime(location);
                }
                
                // Fallback: try to get from the PE header (more reliable for actual build time)
                var buffer = new byte[2048];
                using (var fileStream = new FileStream(location, FileMode.Open, FileAccess.Read))
                {
                    fileStream.Read(buffer, 0, buffer.Length);
                }
                
                var offset = BitConverter.ToInt32(buffer, 60);
                var secondsSince1970 = BitConverter.ToInt32(buffer, offset + 8);
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return epoch.AddSeconds(secondsSince1970);
            }
            catch
            {
                // If all else fails, use current time
                return DateTime.Now;
            }
        }

        private void OpenDiscord()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.gg/rtGXwMn7pX",
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignored
            }
        }

        private void OpenGitHub()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/CouncilOfTsukuyomi/Atomos",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void OpenLicense()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/CouncilOfTsukuyomi/Atomos/blob/main/LICENSE",
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignored
            }
        }

        private void OpenSevenZipExtractor()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/CollapseLauncher/SevenZipExtractor",
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignored
            }
        }

        private void OpenAvalonia()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://avaloniaui.net/",
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignored
            }
        }

        private void OpenReactiveUI()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.reactiveui.net/",
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignored
            }
        }
    }
}