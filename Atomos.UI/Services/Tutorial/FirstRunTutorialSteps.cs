using System;
using System.Collections.Generic;
using System.Linq;
using Atomos.UI.Enums.Tutorial;
using Atomos.UI.Models.Tutorial;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.UI.Services.Tutorial;

public static class FirstRunTutorialSteps
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    public static List<TutorialStep> GetFirstRunSteps(IConfigurationService configurationService)
    {
        return
        [
            new TutorialStep
            {
                Id = "welcome",
                Title = "Welcome to Atomos!",
                Description = "Welcome to Atomos Mod Forwarder! This quick setup will help you get started with managing your mods. We'll guide you through the essential configuration steps to ensure everything works smoothly.\n\nClick 'Next' to continue to the settings.",
                TargetElementName = "SettingsButton",
                Position = TutorialPosition.Center,
                IsRequired = true,
                OnStepCompleted = () =>
                {
                    _logger.Debug("Welcome step completed, should navigate to Settings");
                }
            },


            new TutorialStep
            {
                Id = "download-path",
                Title = "Set Download Path",
                Description = "First, let's set up where Atomos will look for your downloaded mods. Click the 'Add Path' button under to 'Download Path' and select a folder on your computer, this has been highlighted in yellow for you. This is where Atomos will search for mod files when you are downloading.",
                TargetElementName = "DownloadPathSetting",
                Position = TutorialPosition.Top,
                IsRequired = true,
                CanProceed = () =>
                {
                    try
                    {
                        var downloadPathValue = configurationService.ReturnConfigValue(c => c.BackgroundWorker.DownloadPath);
                        
                        if (downloadPathValue is List<string> pathList)
                        {
                            var hasValidPath = pathList.Any(path => !string.IsNullOrWhiteSpace(path));
                            
                            if (hasValidPath)
                            {
                                _logger.Debug("Download path validation passed: {PathCount} paths configured", pathList.Count);
                            }
                            else
                            {
                                _logger.Debug("Download path validation failed: no valid paths in list");
                            }
                            
                            return hasValidPath;
                        }
                        
                        var downloadPath = downloadPathValue?.ToString() ?? string.Empty;
                        var hasPath = !string.IsNullOrEmpty(downloadPath);
                        
                        if (hasPath)
                        {
                            _logger.Debug("Download path validation passed: {DownloadPath}", downloadPath);
                        }
                        else
                        {
                            _logger.Debug("Download path validation failed: path is empty");
                        }
                        
                        return hasPath;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Download path validation failed with exception");
                        return false;
                    }
                }
            },

            new TutorialStep
            {
                Id = "file-linking",
                Title = "File Association (Optional)",
                Description = "Enable 'Double Click File Support' if you want to open mod files directly with Atomos by double-clicking them in Windows Explorer, this has been highlighted in yellow for you. This is optional but convenient for managing your mods.",
                TargetElementName = "FileLinkingEnabled",
                Position = TutorialPosition.Top,
                IsRequired = false
            },

            new TutorialStep
            {
                Id = "auto-start",
                Title = "Auto-Start Options (Optional)",
                Description = "You can configure Atomos to start automatically when your computer boots or when FFXIV launches, this has been highlighted in yellow for you. These settings are optional and can be changed later if needed.",
                TargetElementName = "StartOnBoot",
                Position = TutorialPosition.Top,
                IsRequired = false
            },

            new TutorialStep
            {
                Id = "settings-search",
                Title = "Search Settings",
                Description = "Use this search box to quickly find specific settings when you have many configuration options. Just type what you're looking for and the settings will be filtered automatically!",
                TargetElementName = "SettingsSearchBox",
                Position = TutorialPosition.Center,
                IsRequired = false
            },

            new TutorialStep
            {
                Id = "navigation",
                Title = "Navigation Menu",
                Description = "Use this sidebar to navigate between different sections of the app:\n• Home - Overview\n• Mods - Manage your installed mods\n• Plugins - Configure mod plugins\n• Plugin Data - View plugin information\n\nClick 'Next' to view the Home screen.",
                TargetElementName = "NavigationPanel",
                Position = TutorialPosition.Center,
                IsRequired = false,
                OnStepCompleted = () =>
                {
                    _logger.Debug("Navigation step completed, should navigate to Home");
                }
            },

            new TutorialStep
            {
                Id = "home-overview",
                Title = "Home Screen",
                Description = "This is your Home screen where you'll find quick access to statistics about mod downloads and system status. You can always return here by clicking the Home button in the sidebar.",
                TargetElementName = "HomeMenuItem",
                Position = TutorialPosition.Center,
                IsRequired = false
            },

            new TutorialStep
            {
                Id = "complete",
                Title = "You're All Set!",
                Description = "Congratulations! You've completed the initial setup. Atomos is now ready to help you manage your mods. You can always access settings again from the gear icon, and remember to check the About section if you need help.",
                TargetElementName = "NavigationPanel",
                Position = TutorialPosition.Center,
                IsRequired = false
            }
        ];
    }

    public static List<TutorialStep> GetFeatureTutorialSteps(string featureName, IConfigurationService configurationService)
    {
        return featureName switch
        {
            "mods" => GetModManagementSteps(),
            "plugins" => GetPluginManagementSteps(),
            "settings" => GetAdvancedSettingsSteps(configurationService),
            _ => new List<TutorialStep>()
        };
    }

    private static List<TutorialStep> GetModManagementSteps()
    {
        return
        [
            new TutorialStep
            {
                Id = "mod-list",
                Title = "Mod Management",
                Description = "",
                TargetElementName = "ModList",
                Position = TutorialPosition.Top
            },
            
            new TutorialStep
            {
                Id = "mod-actions",
                Title = "Mod Actions",
                Description = "",
                TargetElementName = "ModList",
                Position = TutorialPosition.Right
            }
        ];
    }

    private static List<TutorialStep> GetPluginManagementSteps()
    {
        return
        [
            new TutorialStep
            {
                Id = "plugin-list",
                Title = "Plugin Management",
                Description = "Plugins extend Atomos functionality. Here you can see installed plugins, configure their settings, and manage their status.",
                TargetElementName = "PluginList",
                Position = TutorialPosition.Top
            }
        ];
    }

    private static List<TutorialStep> GetAdvancedSettingsSteps(IConfigurationService configurationService)
    {
        return
        [
            new TutorialStep
            {
                Id = "advanced-settings",
                Title = "Advanced Settings",
                Description = "These settings allow you to fine-tune Atomos behavior. Most users won't need to change these, but they're available if you need more control.",
                TargetElementName = "AdvancedSettingsPanel",
                Position = TutorialPosition.Left
            }
        ];
    }
}