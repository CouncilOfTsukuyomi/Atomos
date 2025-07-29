
using Atomos.Statistics.Services;
using CommonLib.Enums;
using CommonLib.Interfaces;
using Moq;

namespace Atomos.Statistics.Tests.Services;

public class StatisticsServiceTests : IDisposable
{
    private readonly Mock<IFileStorage> _mockFileStorage;
    private readonly StatisticService _statisticsService;
    private readonly string _tempDatabasePath;

    public StatisticsServiceTests()
    {
        _mockFileStorage = new Mock<IFileStorage>();
        _mockFileStorage.Setup(fs => fs.Exists(It.IsAny<string>())).Returns(false);
        _tempDatabasePath = Path.Combine(Path.GetTempPath(), $"test_userstats_{Guid.NewGuid()}.db");
        
        _statisticsService = new StatisticService(_mockFileStorage.Object, _tempDatabasePath);
    }

    private async Task WaitForOperationsToComplete()
    {
        // Wait for service initialization
        await Task.Delay(1000);
        
        // Force flush operations and clear cache
        await _statisticsService.FlushAndRefreshAsync(TimeSpan.FromSeconds(15));
        
        // Additional wait to ensure database writes are complete
        await Task.Delay(500);
        
        // Clear cache again to ensure fresh reads
        await _statisticsService.RefreshCacheAsync();
    }

    [Fact]
    public async Task IncrementStatAsync_IncrementsModsInstalledCount()
    {
        await WaitForOperationsToComplete();
        
        var initialCount = await _statisticsService.GetStatCountAsync(Stat.ModsInstalled);
        
        await _statisticsService.IncrementStatAsync(Stat.ModsInstalled);
        await WaitForOperationsToComplete();
        
        var updatedCount = await _statisticsService.GetStatCountAsync(Stat.ModsInstalled);

        Assert.Equal(initialCount + 1, updatedCount);
    }

    [Fact]
    public async Task GetStatCountAsync_ReturnsCorrectCount()
    {
        await WaitForOperationsToComplete();
        
        await _statisticsService.IncrementStatAsync(Stat.ModsInstalled);
        await _statisticsService.IncrementStatAsync(Stat.ModsInstalled);
        await WaitForOperationsToComplete();

        var count = await _statisticsService.GetStatCountAsync(Stat.ModsInstalled);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task EnsureDatabaseExists_CreatesDatabaseFile()
    {
        await WaitForOperationsToComplete();
        
        await _statisticsService.IncrementStatAsync(Stat.ModsInstalled);
        await WaitForOperationsToComplete();

        Assert.True(File.Exists(_tempDatabasePath), "Database file should be created");
    }
    
    [Fact]
    public async Task RecordModInstallationAsync_AddsNewModInstallationRecord()
    {
        await WaitForOperationsToComplete();
        
        string modName = "Test Mod";
        await _statisticsService.RecordModInstallationAsync(modName);
        await WaitForOperationsToComplete();

        var mostRecentMod = await _statisticsService.GetMostRecentModInstallationAsync();
        Assert.NotNull(mostRecentMod);
        Assert.Equal(modName, mostRecentMod.ModName);
    }

    [Fact]
    public async Task GetMostRecentModInstallationAsync_ReturnsMostRecentlyInstalledMod()
    {
        await WaitForOperationsToComplete();
        
        string modName1 = "First Mod";
        string modName2 = "Second Mod";

        await _statisticsService.RecordModInstallationAsync(modName1);
        await Task.Delay(200);
        await _statisticsService.RecordModInstallationAsync(modName2);
        await WaitForOperationsToComplete();

        var mostRecentMod = await _statisticsService.GetMostRecentModInstallationAsync();

        Assert.NotNull(mostRecentMod);
        Assert.Equal(modName2, mostRecentMod.ModName);
    }

    public void Dispose()
    {
        _statisticsService?.Dispose();
        
        if (File.Exists(_tempDatabasePath))
        {
            try
            {
                File.Delete(_tempDatabasePath);
            }
            catch
            {
                // Temp file cleanup will happen eventually
            }
        }
    }
}