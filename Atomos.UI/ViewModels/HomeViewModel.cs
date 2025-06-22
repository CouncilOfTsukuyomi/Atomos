using System;
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
    

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }
    
    public HomeViewModel(
        IStatisticService statisticService,
        IWebSocketClient webSocketClient,
        IFileSizeService fileSizeService)
    {
        _statisticService = statisticService;
        _webSocketClient = webSocketClient;
        _fileSizeService = fileSizeService;

        InfoItems = new ObservableCollection<InfoItem>();
        
        _webSocketClient.ModInstalled += OnModInstalled;

        _ = LoadStatisticsAsync();
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