﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.Models;
using CommonLib.Consts;
using CommonLib.Enums;
using CommonLib.Interfaces;
using CommonLib.Models;
using NLog;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class HomeViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IStatisticService _statisticService;
    private readonly IXmaModDisplay _xmaModDisplay;
    private readonly IFileSizeService _fileSizeService;
    private readonly CompositeDisposable _disposables = new();
    private readonly SemaphoreSlim _statsSemaphore = new(1, 1);

    private readonly IWebSocketClient _webSocketClient;

    private ObservableCollection<InfoItem> _infoItems;
    public ObservableCollection<InfoItem> InfoItems
    {
        get => _infoItems;
        set => this.RaiseAndSetIfChanged(ref _infoItems, value);
    }

    private ObservableCollection<XmaMods> _recentMods;
    public ObservableCollection<XmaMods> RecentMods
    {
        get => _recentMods;
        set => this.RaiseAndSetIfChanged(ref _recentMods, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }
    
    public HomeViewModel(
        IStatisticService statisticService,
        IXmaModDisplay xmaModDisplay,
        IWebSocketClient webSocketClient,
        IFileSizeService fileSizeService)
    {
        _statisticService = statisticService;
        _xmaModDisplay = xmaModDisplay;
        _webSocketClient = webSocketClient;
        _fileSizeService = fileSizeService;

        InfoItems = new ObservableCollection<InfoItem>();
        RecentMods = new ObservableCollection<XmaMods>();
        
        _webSocketClient.ModInstalled += OnModInstalled;

        _ = LoadStatisticsAsync();

        Observable.Timer(TimeSpan.Zero, TimeSpan.FromMinutes(5))
            .SelectMany(_ => Observable.FromAsync(RefreshRecentModsAsync))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe()
            .DisposeWith(_disposables);
    }

    private async void OnModInstalled(object sender, EventArgs e)
    {
        RxApp.MainThreadScheduler.ScheduleAsync(async (_, __) =>
        {
            await _statisticService.FlushAndRefreshAsync(TimeSpan.FromSeconds(2));
            await LoadStatisticsAsync();
        });
    }

    private async Task RefreshRecentModsAsync()
    {
        try
        {
            IsLoading = true;

            var modsFromSource = await _xmaModDisplay.GetRecentMods();

            // Gather only one instance per ModUrl from the source data
            var distinctSourceMods = modsFromSource
                .GroupBy(m => m.ModUrl)
                .Select(g => g.First())
                .ToList();

            var existingModUrls = RecentMods
                .Select(rm => rm.ModUrl)
                .ToHashSet();

            foreach (var mod in distinctSourceMods)
            {
                if (!existingModUrls.Contains(mod.ModUrl))
                {
                    RecentMods.Add(mod);
                }
            }

            _logger.Debug("Successfully updated the recent mods list without duplicates.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unable to retrieve or log recent mods");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadStatisticsAsync()
    {
        if (!await _statsSemaphore.WaitAsync(TimeSpan.FromSeconds(10)))
            return;

        try
        {
            var newItems = new ObservableCollection<InfoItem>
            {
                new("Total Mods Installed", (await _statisticService.GetStatCountAsync(Stat.ModsInstalled)).ToString()),
                new("Unique Mods Installed", (await _statisticService.GetUniqueModsInstalledCountAsync()).ToString())
            };

            var modsInstalledToday = await _statisticService.GetModsInstalledTodayAsync();
            newItems.Add(new InfoItem("Mods Installed Today", modsInstalledToday.ToString()));

            var lastModInstallation = await _statisticService.GetMostRecentModInstallationAsync();
            newItems.Add(lastModInstallation != null
                ? new InfoItem("Last Mod Installed", lastModInstallation.ModName)
                : new InfoItem("Last Mod Installed", "None"));

            var modsFolderSizeLabel = _fileSizeService.GetFolderSizeLabel(ConfigurationConsts.ModsPath);
            newItems.Add(new InfoItem("Mods Folder Size", modsFolderSizeLabel));
            
            InfoItems = newItems;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load statistics in HomeViewModel.");
        }
        finally
        {
            _statsSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _webSocketClient.ModInstalled -= OnModInstalled;
        _disposables.Dispose();
    }
}