using System;
using System.IO;
using AutoMapper;
using CommonLib.Services;
using CommonLib.Enums;
using CommonLib.Interfaces;
using Moq;
using Xunit;

namespace Atomos.BackgroundWorker.Tests.Services;

public class TexToolsHelperImmediateUpdateTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _fakeTexToolsDir;
    private readonly string _fakeConsoleExe;

    public TexToolsHelperImmediateUpdateTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AtomosTests", Guid.NewGuid().ToString("N"));
        _fakeTexToolsDir = Path.Combine(_tempRoot, "FFXIV_TexTools");
        Directory.CreateDirectory(_fakeTexToolsDir);
        _fakeConsoleExe = Path.Combine(_fakeTexToolsDir, "ConsoleTools.exe");
        // Create a zero-byte file to simulate existence (we won't execute it)
        File.WriteAllBytes(_fakeConsoleExe, Array.Empty<byte>());
    }

    [Fact]
    public void SetTexToolConsolePath_UpdatesConfigImmediately()
    {
        // Arrange mocks
        var regHelper = new Mock<IRegistryHelper>(MockBehavior.Strict);
        regHelper.SetupGet(r => r.IsRegistrySupported).Returns(true);
        // Return the parent directory so helper will combine with FFXIV_TexTools/ConsoleTools.exe
        regHelper.Setup(r => r.GetTexToolRegistryValue()).Returns(_tempRoot);

        var fsHelper = new Mock<IFileSystemHelper>(MockBehavior.Strict);
        fsHelper.Setup(h => h.FileExists(_fakeConsoleExe)).Returns(true);
        fsHelper.Setup(h => h.GetStandardTexToolsPaths()).Returns(Array.Empty<string>());

        // Real dependencies for ConfigurationService
        var fileStorage = new FileStorage();
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<ConvertConfiguration>());
        var mapper = mapperConfig.CreateMapper();

        var dbPath = Path.Combine(_tempRoot, "config-tests.db");
        var configurationService = new ConfigurationService(fileStorage, mapper, dbPath);

        var helper = new TexToolsHelper(regHelper.Object, configurationService, fsHelper.Object);

        // Act
        var status = helper.SetTexToolConsolePath();

        // Assert
        Assert.Equal(TexToolsStatus.Found, status);
        var configuredPath = (string)configurationService.ReturnConfigValue(m => m.BackgroundWorker.TexToolPath);
        Assert.Equal(_fakeConsoleExe, configuredPath);
    }

    [Fact]
    public void SetTexToolConsolePath_WhenAlreadyConfigured_ReturnsAlreadyConfigured()
    {
        // Arrange
        var regHelper = new Mock<IRegistryHelper>(MockBehavior.Loose);
        regHelper.SetupGet(r => r.IsRegistrySupported).Returns(true);
        regHelper.Setup(r => r.GetTexToolRegistryValue()).Returns(_tempRoot);

        var fsHelper = new Mock<IFileSystemHelper>(MockBehavior.Loose);
        fsHelper.Setup(h => h.FileExists(_fakeConsoleExe)).Returns(true);
        fsHelper.Setup(h => h.GetStandardTexToolsPaths()).Returns(Array.Empty<string>());

        var fileStorage = new FileStorage();
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<ConvertConfiguration>());
        var mapper = mapperConfig.CreateMapper();

        var dbPath = Path.Combine(_tempRoot, "config-tests-2.db");
        var configurationService = new ConfigurationService(fileStorage, mapper, dbPath);

        var helper = new TexToolsHelper(regHelper.Object, configurationService, fsHelper.Object);

        // First time should configure
        var first = helper.SetTexToolConsolePath();
        Assert.Equal(TexToolsStatus.Found, first);

        // Act: second time should detect it's already configured
        var second = helper.SetTexToolConsolePath();

        // Assert
        Assert.Equal(TexToolsStatus.AlreadyConfigured, second);
        var configuredPath = (string)configurationService.ReturnConfigValue(m => m.BackgroundWorker.TexToolPath);
        Assert.Equal(_fakeConsoleExe, configuredPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // ignore cleanup errors in CI
        }
    }
}
