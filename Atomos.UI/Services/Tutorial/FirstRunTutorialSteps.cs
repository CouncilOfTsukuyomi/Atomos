using System;
using System.Collections;
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
                        _logger.Debug("Starting download path validation...");
                        
                        var downloadPathValue = configurationService.ReturnConfigValue(c => c.BackgroundWorker.DownloadPath);

                        _logger.Debug("Download path validation - Raw value type: {ValueType}, Value: {Value}", 
                            downloadPathValue?.GetType()?.Name ?? "null", downloadPathValue);
                        
                        if (downloadPathValue != null)
                        {
                            if (downloadPathValue is List<string> list)
                            {
                                _logger.Debug("Value is List<string> with {Count} items: [{Items}]", 
                                    list.Count, string.Join(", ", list.Select(x => $"'{x}'")));
                            }
                            else if (downloadPathValue is IEnumerable enumerable && !(downloadPathValue is string))
                            {
                                var items = enumerable.Cast<object>().Select(x => $"'{x}'").ToList();
                                _logger.Debug("Value is IEnumerable with {Count} items: [{Items}]", 
                                    items.Count, string.Join(", ", items));
                            }
                            else
                            {
                                _logger.Debug("Value is {Type}: '{Value}'", downloadPathValue.GetType().Name, downloadPathValue);
                            }
                        }
                        
                        if (downloadPathValue == null)
                        {
                            _logger.Debug("Download path is null, returning false");
                            return false;
                        }
                        
                        if (downloadPathValue is List<string> pathList)
                        {
                            _logger.Debug("Processing as List<string> with {Count} items", pathList.Count);
                            
                            var validPaths = pathList.Where(path => 
                            {
                                var trimmed = path?.Trim();
                                var isValid = !string.IsNullOrWhiteSpace(trimmed);
                                _logger.Debug("Path '{Path}' -> Trimmed: '{Trimmed}' -> Valid: {IsValid}", path, trimmed, isValid);
                                return isValid;
                            }).ToList();
                            
                            var hasValidPath = validPaths.Any();

                            _logger.Debug("Download path validation (List<string>) - PathCount: {PathCount}, ValidPathCount: {ValidPathCount}, HasValidPath: {HasValidPath}, Paths: [{Paths}]", 
                                pathList.Count, validPaths.Count, hasValidPath, string.Join(", ", validPaths));

                            return hasValidPath;
                        }
                        
                        if (downloadPathValue is IEnumerable enumerable2 && !(downloadPathValue is string))
                        {
                            _logger.Debug("Processing as IEnumerable (not string)");
                            
                            var enumerablePaths = enumerable2.Cast<object>()
                                .Select(x => x?.ToString()?.Trim() ?? string.Empty)
                                .Where(path => !string.IsNullOrWhiteSpace(path))
                                .ToList();
                            var hasValidPath = enumerablePaths.Any();

                            _logger.Debug("Download path validation (IEnumerable) - PathCount: {PathCount}, HasValidPath: {HasValidPath}, Paths: [{Paths}]", 
                                enumerablePaths.Count, hasValidPath, string.Join(", ", enumerablePaths));

                            return hasValidPath;
                        }
                        
                        _logger.Debug("Processing as string");
                        var downloadPath = downloadPathValue?.ToString()?.Trim() ?? string.Empty;
                        var hasPath = !string.IsNullOrWhiteSpace(downloadPath);

                        _logger.Debug("Download path validation (string) - HasPath: {HasPath}, DownloadPath: '{DownloadPath}'", 
                            hasPath, downloadPath);

                        return hasPath;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Download path validation failed with exception");
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
                Description = "This is your plugin hub! Plugins add new features and connect to different mod websites. You can view all your installed plugins, check their status, and manage their settings from here.",
                TargetElementName = "PluginListContainer",
                Position = TutorialPosition.Top
            },
            
            new TutorialStep
            {
                Id = "plugin-config",
                Title = "Plugin Settings",
                Description = "Each plugin has its own settings that you can customise. Click this button to open the settings panel and configure how the plugin works.",
                TargetElementName = "PluginSettingsButton",
                Position = TutorialPosition.Center
            },
            
            new TutorialStep
            {
                Id = "plugin-status",
                Title = "Plugin Status",
                Description = "This shows whether a plugin is currently active or disabled. You can temporarily turn plugins on or off without uninstalling them",
                TargetElementName = "PluginStatus",
                Position = TutorialPosition.Center
            },
            
            new TutorialStep
            {
                Id = "plugin-enable",
                Title = "Enable/Disable Plugins",
                Description = "Toggle plugins on or off with this button. When enabled, the plugin will start working and you can modify its settings. When disabled, it won't affect your mod downloads.",
                TargetElementName = "PluginEnableButton",
                Position = TutorialPosition.Center
            },
            
            new TutorialStep
            {
                Id = "plugin-view-mods",
                Title = "Browse Plugin Mods",
                Description = "Ready to find some mods? Each plugin connects to different mod websites. Click 'Plugin Data' in the menu to browse and download mods from the websites your plugins support.",
                TargetElementName = "PluginDataMenuItem",
                Position = TutorialPosition.Center
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