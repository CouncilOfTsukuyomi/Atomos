﻿using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.Models;
using CommonLib.Consts;
using CommonLib.Enums;
using CommonLib.Interfaces;
using NLog;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class HomeViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IStatisticService _statisticService;
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

    private ObservableCollection<InfoItem> _regularStats;
    public ObservableCollection<InfoItem> RegularStats
    {
        get => _regularStats;
        set => this.RaiseAndSetIfChanged(ref _regularStats, value);
    }

    private InfoItem _lastModInstalled;
    public InfoItem LastModInstalled
    {
        get => _lastModInstalled;
        set => this.RaiseAndSetIfChanged(ref _lastModInstalled, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public ReactiveCommand<Unit, Unit> OpenModsFolderCommand { get; }
    
    public HomeViewModel(
        IStatisticService statisticService,
        IWebSocketClient webSocketClient,
        IFileSizeService fileSizeService)
    {
        _statisticService = statisticService;
        _webSocketClient = webSocketClient;
        _fileSizeService = fileSizeService;

        InfoItems = new ObservableCollection<InfoItem>();
        RegularStats = new ObservableCollection<InfoItem>();

        OpenModsFolderCommand = ReactiveCommand.Create(OpenModsFolder);
        
        _webSocketClient.ModInstalled += OnModInstalled;

        _ = LoadStatisticsAsync();
    }

    private void OpenModsFolder()
    {
        try
        {
            var modsPath = ConfigurationConsts.ModsPath;
            
            modsPath = Path.GetFullPath(modsPath);
        
            _logger.Info("Opening mods folder: {ModsPath}", modsPath);
            
            if (!Directory.Exists(modsPath))
            {
                _logger.Warn("Mods directory does not exist, creating: {ModsPath}", modsPath);
                Directory.CreateDirectory(modsPath);
            }
        
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{modsPath}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{modsPath}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{modsPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open mods folder");
        }
    }


    private async void OnModInstalled(object sender, EventArgs e)
    {
        RxApp.MainThreadScheduler.ScheduleAsync(async (_, __) =>
        {
            await _statisticService.FlushAndRefreshAsync(TimeSpan.FromSeconds(2));
            await LoadStatisticsAsync();
        });
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

            var modsFolderSizeLabel = _fileSizeService.GetFolderSizeLabel(ConfigurationConsts.ModsPath);
            newItems.Add(new InfoItem("Mods Folder Size", modsFolderSizeLabel, OpenModsFolderCommand));

            // Separate the regular stats from the last mod installed
            RegularStats = newItems;

            var lastModInstallation = await _statisticService.GetMostRecentModInstallationAsync();
            LastModInstalled = lastModInstallation != null
                ? new InfoItem("Last Mod Installed", lastModInstallation.ModName)
                : new InfoItem("Last Mod Installed", "None");

            // Keep the full collection for compatibility
            var allItems = new ObservableCollection<InfoItem>(newItems);
            allItems.Add(LastModInstalled);
            InfoItems = allItems;
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