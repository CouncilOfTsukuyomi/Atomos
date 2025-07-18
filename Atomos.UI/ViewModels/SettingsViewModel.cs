using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Atomos.UI.Helpers;
using Atomos.UI.Interfaces;
using CommonLib.Attributes;
using CommonLib.Interfaces;
using CommonLib.Models;
using Newtonsoft.Json;
using NLog;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IWebSocketClient _webSocketClient;
    
    // Dictionary to map property names to tutorial-friendly names
    private readonly Dictionary<string, string> _tutorialNameMap = new()
    {
        { "DownloadPath", "DownloadPathSetting" },
        { "StartOnBoot", "StartOnBoot" },
        { "FileLinkingEnabled", "FileLinkingEnabled" },
        { "EnableSentry", "EnableSentry" },
        { "EnableDebugLogs", "EnableDebugLogs" }
    };
    
    // Dictionary to map tutorial element names to their group names
    private readonly Dictionary<string, string> _tutorialElementToGroupMap = new()
    {
        { "DownloadPathSetting", "Pathing" },
        { "StartOnBoot", "General" },
        { "FileLinkingEnabled", "General" },
        { "EnableSentry", "General" },
        { "EnableDebugLogs", "Advanced" } 
    };

    
    public ObservableCollection<ConfigurationGroup> AllGroups { get; } = new();
        
    private ObservableCollection<ConfigurationGroup> _filteredGroups = new();
    public ObservableCollection<ConfigurationGroup> FilteredGroups
    {
        get => _filteredGroups;
        set => this.RaiseAndSetIfChanged(ref _filteredGroups, value);
    }

    private ConfigurationGroup? _selectedGroup;
    public ConfigurationGroup? SelectedGroup
    {
        get => _selectedGroup;
        set => this.RaiseAndSetIfChanged(ref _selectedGroup, value);
    }

    private string _searchTerm = string.Empty;
    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchTerm, value);
            FilterSettings();
        }
    }

    public SettingsViewModel(
        IConfigurationService configurationService,
        IFileDialogService fileDialogService,
        IWebSocketClient webSocketClient)
    {
        _configurationService = configurationService;
        _fileDialogService = fileDialogService;
        _webSocketClient = webSocketClient;

        LoadConfigurationSettings();
    }

    private void LoadConfigurationSettings()
    {
        var configurationModel = _configurationService.GetConfiguration();
        LoadPropertiesFromModel(configurationModel);
        FilteredGroups = new ObservableCollection<ConfigurationGroup>(AllGroups);
        
        // Set the first group as selected by default
        if (FilteredGroups.Any())
        {
            SelectedGroup = FilteredGroups.First();
        }
    }

    public void NavigateToElement(string elementName)
    {
        _logger.Debug("NavigateToElement called for: {ElementName}", elementName);
        
        // Clear search term to show all groups
        SearchTerm = string.Empty;
        
        // Find the group that contains this element
        if (_tutorialElementToGroupMap.TryGetValue(elementName, out var groupName))
        {
            var targetGroup = AllGroups.FirstOrDefault(g => g.GroupName == groupName);
            if (targetGroup != null)
            {
                _logger.Debug("Switching to group: {GroupName}", groupName);
                SelectedGroup = targetGroup;
                return;
            }
        }
        
        // Fallback: search through all groups for the element
        foreach (var group in AllGroups)
        {
            var hasElement = group.Properties.Any(p => 
                p.TutorialName == elementName || 
                (p.TutorialName == null && _tutorialNameMap.ContainsValue(elementName) && 
                 _tutorialNameMap.FirstOrDefault(kvp => kvp.Value == elementName).Key == p.PropertyInfo.Name));
            
            if (hasElement)
            {
                _logger.Debug("Found element {ElementName} in group: {GroupName}", elementName, group.GroupName);
                SelectedGroup = group;
                return;
            }
        }
        
        _logger.Warn("Could not find group for element: {ElementName}", elementName);
    }

    private void LoadPropertiesFromModel(
        object model,
        ConfigurationPropertyDescriptor parentDescriptor = null,
        string parentGroupName = null)
    {
        var properties = model.GetType().GetProperties();

        foreach (var prop in properties)
        {
            // Skip properties marked with [ExcludeFromSettingsUI]
            if (prop.GetCustomAttribute<ExcludeFromSettingsUIAttribute>() != null)
                continue;

            var displayAttr = prop.GetCustomAttribute<DisplayAttribute>();
            var displayName = displayAttr?.Name ?? prop.Name;
            var description = displayAttr?.Description;
            var groupName = displayAttr?.GroupName ?? parentGroupName ?? "General";

            _logger.Debug(
                "Processing property '{PropertyName}' of type '{PropertyType}' in group '{GroupName}'",
                prop.Name,
                prop.PropertyType.Name,
                groupName);

            if (IsNestedModel(prop.PropertyType))
            {
                var nestedModelInstance = prop.GetValue(model);
                var nestedDescriptor = new ConfigurationPropertyDescriptor
                {
                    DisplayName = displayName,
                    Description = description,
                    PropertyInfo = prop,
                    ModelInstance = model,
                    ParentDescriptor = parentDescriptor,
                    GroupName = groupName
                };
                LoadPropertiesFromModel(nestedModelInstance, nestedDescriptor, groupName);
            }
            else
            {
                var descriptor = new ConfigurationPropertyDescriptor
                {
                    DisplayName = displayName,
                    Description = description,
                    PropertyInfo = prop,
                    ModelInstance = model,
                    ParentDescriptor = parentDescriptor,
                    GroupName = groupName
                };

                descriptor.Value = prop.GetValue(model);

                // Set tutorial name if available
                if (_tutorialNameMap.TryGetValue(prop.Name, out var tutorialName))
                {
                    descriptor.TutorialName = tutorialName;
                    _logger.Debug("Assigned tutorial name '{TutorialName}' to property '{PropertyName}'", tutorialName, prop.Name);
                }

                // If the property is a path, attach a "BrowseCommand"
                if ((prop.PropertyType == typeof(string) || prop.PropertyType == typeof(List<string>)) &&
                    displayName.Contains("Path", StringComparison.OrdinalIgnoreCase))
                {
                    descriptor.BrowseCommand = ReactiveCommand.CreateFromTask(
                        () => ExecuteBrowseCommand(descriptor)
                    );
                }

                // Get the group from AllGroups
                var group = AllGroups.FirstOrDefault(g => g.GroupName == groupName);
                if (group == null)
                {
                    group = new ConfigurationGroup(groupName);
                    AllGroups.Add(group);
                }

                // Check for duplicates
                var existingDescriptor = group.Properties
                    .FirstOrDefault(d => d.PropertyInfo.Name == descriptor.PropertyInfo.Name);

                if (existingDescriptor == null)
                {
                    group.Properties.Add(descriptor);
                }
                else
                {
                    _logger.Warn(
                        "Property '{PropertyName}' is already added to group '{GroupName}'. Skipping duplicate.",
                        descriptor.PropertyInfo.Name,
                        groupName
                    );
                }

                // Subscribe to changes
                descriptor.WhenAnyValue(d => d.Value)
                    .Skip(1)
                    .Throttle(TimeSpan.FromMilliseconds(200))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(_ => SaveSettings(descriptor));
            }
        }
    }

    private bool IsNestedModel(Type type)
    {
        return type.Namespace == "CommonLib.Models"
               && type.IsClass
               && !type.IsPrimitive
               && !type.IsEnum
               && type != typeof(string)
               && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    private async Task ExecuteBrowseCommand(ConfigurationPropertyDescriptor descriptor)
    {
        try
        {
            string initialDirectory = null;

            if (descriptor.Value is string path && !string.IsNullOrEmpty(path))
            {
                initialDirectory = System.IO.Path.GetDirectoryName(path);
            }
            else if (descriptor.Value is List<string> paths && paths.Any())
            {
                initialDirectory = System.IO.Path.GetDirectoryName(paths.Last());
            }

            if (descriptor.PropertyInfo.PropertyType == typeof(string))
            {
                var selectedPath = await _fileDialogService.OpenFolderAsync(
                    initialDirectory,
                    $"Select {descriptor.DisplayName}"
                );
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    descriptor.Value = selectedPath;
                }
            }
            else if (descriptor.PropertyInfo.PropertyType == typeof(List<string>))
            {
                var selectedPaths = await _fileDialogService.OpenFoldersAsync(
                    initialDirectory,
                    $"Select {descriptor.DisplayName}"
                );

                if (selectedPaths != null && selectedPaths.Any())
                {
                    var existingPaths = descriptor.Value as List<string> ?? new List<string>();
                    var newPathsList = existingPaths.Union(selectedPaths).ToList();
                    descriptor.Value = newPathsList;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error executing browse command for {DisplayName}", descriptor.DisplayName);
        }
    }

    private void SaveSettings(ConfigurationPropertyDescriptor descriptor)
    {
        try
        {
            var propertyPath = GetPropertyPath(descriptor);

            var taskId = Guid.NewGuid().ToString();
            var configurationChange = new
            {
                PropertyPath = propertyPath,
                NewValue = descriptor.Value
            };

            var message = WebSocketMessage.CreateStatus(
                taskId,
                WebSocketMessageStatus.InProgress,
                $"Configuration changed: {propertyPath}"
            );

            message.Type = WebSocketMessageType.ConfigurationChange;
            message.Message = JsonConvert.SerializeObject(configurationChange);

            _ = _webSocketClient.SendMessageAsync(message, "/config").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error saving settings");
        }
    }

    private string GetPropertyPath(ConfigurationPropertyDescriptor descriptor)
    {
        var pathSegments = new List<string>();
        var currentDescriptor = descriptor;

        while (currentDescriptor != null)
        {
            pathSegments.Insert(0, currentDescriptor.PropertyInfo.Name);
            currentDescriptor = currentDescriptor.ParentDescriptor;
        }

        return string.Join(".", pathSegments);
    }

    private void FilterSettings()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm))
        {
            FilteredGroups = new ObservableCollection<ConfigurationGroup>(AllGroups);
            return;
        }

        var term = SearchTerm.Trim();

        var newGroups = new List<ConfigurationGroup>();
        foreach (var group in AllGroups)
        {
            var matchingProperties = group.Properties
                .Where(pd => MatchesSearch(pd, term))
                .ToList();

            if (matchingProperties.Any())
            {
                var newGroup = new ConfigurationGroup(group.GroupName);
                foreach (var match in matchingProperties)
                {
                    newGroup.Properties.Add(match);
                }
                newGroups.Add(newGroup);
            }
        }

        FilteredGroups = new ObservableCollection<ConfigurationGroup>(newGroups);
    }

    private bool MatchesSearch(ConfigurationPropertyDescriptor descriptor, string term)
    {
        bool inName = descriptor.DisplayName
            .Contains(term, StringComparison.OrdinalIgnoreCase);

        bool inDesc = descriptor.Description != null &&
                      descriptor.Description.Contains(term, StringComparison.OrdinalIgnoreCase);

        return inName || inDesc;
    }
}