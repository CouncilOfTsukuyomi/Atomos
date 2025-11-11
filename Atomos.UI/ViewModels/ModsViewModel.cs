using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using CommonLib.Interfaces;
using CommonLib.Models;
using NLog;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class ModsViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IStatisticService _statisticService;
    private readonly CompositeDisposable _disposables = new();
    
    private readonly IWebSocketClient _webSocketClient;

    private ObservableCollection<ModInstallationRecord> _installedMods;
    /// <summary>
    /// All installed mods (unfiltered)
    /// </summary>
    public ObservableCollection<ModInstallationRecord> InstalledMods
    {
        get => _installedMods;
        set => this.RaiseAndSetIfChanged(ref _installedMods, value);
    }

    private ObservableCollection<ModInstallationRecord> _filteredMods;
    /// <summary>
    /// Filtered mods based on SearchTerm.
    /// </summary>
    public ObservableCollection<ModInstallationRecord> FilteredMods
    {
        get => _filteredMods;
        set => this.RaiseAndSetIfChanged(ref _filteredMods, value);
    }

    private string _searchTerm;
    /// <summary>
    /// Text used to filter the mods.
    /// Updates FilteredMods whenever it changes.
    /// </summary>
    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchTerm, value);
            FilterMods();
        }
    }

    public ModsViewModel(IStatisticService statisticService, IWebSocketClient webSocketClient)
    {
        _statisticService = statisticService;
        _webSocketClient = webSocketClient;

        InstalledMods = new ObservableCollection<ModInstallationRecord>();
        FilteredMods = new ObservableCollection<ModInstallationRecord>();

        // Subscribe to ModInstalled so we refresh the list whenever a mod is installed.
        _webSocketClient.ModInstalled += OnModInstalled;

        // Periodically refresh installed mods
        Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30))
            .SelectMany(_ => Observable.FromAsync(LoadInstalledModsAsync))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe()
            .DisposeWith(_disposables);
    }

    private async void OnModInstalled(object sender, EventArgs e)
    {
        await LoadInstalledModsAsync();
    }

    /// <summary>
    /// Loads installed mods from the service and updates local collections.
    /// </summary>
    private async Task LoadInstalledModsAsync()
    {
        try
        {
            var fetchedMods = await _statisticService.GetAllInstalledModsAsync();

            if (AreSame(InstalledMods, fetchedMods))
                return;
            InstalledMods = new ObservableCollection<ModInstallationRecord>(fetchedMods);

            _logger.Debug("Loaded {Count} installed mods", InstalledMods.Count);
            
            FilterMods();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load installed mods in ModsViewModel.");
        }
    }

    /// <summary>
    /// Applies the search term to the InstalledMods and updates FilteredMods.
    /// </summary>
    private void FilterMods()
    {
        var filter = SearchTerm?.Trim() ?? string.Empty;
        var results = InstalledMods
            .Where(m => m.ModName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        FilteredMods.Clear();
        foreach (var mod in results)
        {
            FilteredMods.Add(mod);
        }
    }

    private bool AreSame(
        ObservableCollection<ModInstallationRecord> current,
        IEnumerable<ModInstallationRecord> incoming)
    {
        var incomingList = incoming as IList<ModInstallationRecord> ?? incoming.ToList();
        if (current.Count != incomingList.Count)
            return false;

        // Compare items individually by ModName
        return !current
            .Where((t, i) => !string.Equals(t.ModName, incomingList[i].ModName, StringComparison.Ordinal))
            .Any();
    }

    public void Dispose()
    {
        _webSocketClient.ModInstalled -= OnModInstalled;
        _disposables.Dispose();
    }
}