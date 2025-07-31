using Atomos.Statistics.Services;

namespace Atomos.Statistics.Tests.Services;

public class FileSizeServiceTests : IDisposable
{
    private readonly FileSizeService _fileSizeService;
    private readonly string _tempDirectory;

    public FileSizeServiceTests()
    {
        _fileSizeService = new FileSizeService();
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"FileSizeServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void GetFolderSizeLabel_NonExistentFolder_ReturnsZeroBytes()
    {
        var nonExistentPath = Path.Combine(_tempDirectory, "non_existent_folder");
        
        var result = _fileSizeService.GetFolderSizeLabel(nonExistentPath);
        
        Assert.Equal("0 B", result);
    }

    [Fact]
    public void GetFolderSizeLabel_EmptyFolder_ReturnsZeroBytes()
    {
        var emptyFolderPath = Path.Combine(_tempDirectory, "empty_folder");
        Directory.CreateDirectory(emptyFolderPath);
        
        var result = _fileSizeService.GetFolderSizeLabel(emptyFolderPath);
        
        Assert.Equal("0 B", result);
    }

    [Fact]
    public void GetFolderSizeLabel_SingleSmallFile_ReturnsBytesFormat()
    {
        var folderPath = Path.Combine(_tempDirectory, "small_file_folder");
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, "small_file.txt");
        
        File.WriteAllText(filePath, "Hello World!");
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal("12 B", result);
    }

    [Fact]
    public void GetFolderSizeLabel_FileExactly1KB_ReturnsKBFormat()
    {
        var folderPath = Path.Combine(_tempDirectory, "kb_folder");
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, "1kb_file.txt");
        
        var content = new string('A', 1024);
        File.WriteAllText(filePath, content);
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal("1.00 KB", result);
    }

    [Fact]
    public void GetFolderSizeLabel_FileExactly1MB_ReturnsMBFormat()
    {
        var folderPath = Path.Combine(_tempDirectory, "mb_folder");
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, "1mb_file.txt");
        
        var content = new string('A', 1024 * 1024);
        File.WriteAllText(filePath, content);
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal("1.00 MB", result);
    }

    [Fact]
    public void GetFolderSizeLabel_MultipleFiles_ReturnsCorrectSum()
    {
        var folderPath = Path.Combine(_tempDirectory, "multiple_files_folder");
        Directory.CreateDirectory(folderPath);
        
        File.WriteAllText(Path.Combine(folderPath, "file1.txt"), new string('A', 512));
        File.WriteAllText(Path.Combine(folderPath, "file2.txt"), new string('B', 512));
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal("1.00 KB", result);
    }

    [Fact]
    public void GetFolderSizeLabel_NestedFolders_ReturnsRecursiveSize()
    {
        var rootFolderPath = Path.Combine(_tempDirectory, "nested_root");
        var subFolderPath = Path.Combine(rootFolderPath, "subfolder");
        var deepFolderPath = Path.Combine(subFolderPath, "deep");
        
        Directory.CreateDirectory(deepFolderPath);
        
        File.WriteAllText(Path.Combine(rootFolderPath, "root_file.txt"), new string('A', 256));
        File.WriteAllText(Path.Combine(subFolderPath, "sub_file.txt"), new string('B', 256));
        File.WriteAllText(Path.Combine(deepFolderPath, "deep_file.txt"), new string('C', 512));
        
        var result = _fileSizeService.GetFolderSizeLabel(rootFolderPath);
        
        Assert.Equal("1.00 KB", result);
    }

    [Fact]
    public void GetFolderSizeLabel_LargeFile_ReturnsGBFormat()
    {
        var folderPath = Path.Combine(_tempDirectory, "gb_folder");
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, "large_file.txt");
        
        using var fileStream = new FileStream(filePath, FileMode.Create);
        fileStream.SetLength(2L * 1024 * 1024 * 1024);
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal("2.00 GB", result);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1572864, "1.50 MB")]
    [InlineData(1073741824, "1.00 GB")]
    [InlineData(1610612736, "1.50 GB")]
    public void GetFolderSizeLabel_VariousSizes_ReturnsCorrectFormat(long bytes, string expected)
    {
        var folderPath = Path.Combine(_tempDirectory, $"test_folder_{bytes}");
        Directory.CreateDirectory(folderPath);
        
        if (bytes > 0)
        {
            var filePath = Path.Combine(folderPath, "test_file.txt");
            using var fileStream = new FileStream(filePath, FileMode.Create);
            fileStream.SetLength(bytes);
        }
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetFolderSizeLabel_MixedFileTypes_ReturnsCorrectSize()
    {
        var folderPath = Path.Combine(_tempDirectory, "mixed_types_folder");
        Directory.CreateDirectory(folderPath);
        
        File.WriteAllText(Path.Combine(folderPath, "text.txt"), "Hello");
        File.WriteAllBytes(Path.Combine(folderPath, "binary.dat"), new byte[] { 1, 2, 3, 4, 5 });
        File.Create(Path.Combine(folderPath, "empty_file.txt")).Dispose();
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal("10 B", result);
    }

    [Fact]
    public void GetFolderSizeLabel_SymbolicLinks_HandlesGracefully()
    {
        var folderPath = Path.Combine(_tempDirectory, "symlink_folder");
        Directory.CreateDirectory(folderPath);
        
        var realFilePath = Path.Combine(folderPath, "real_file.txt");
        File.WriteAllText(realFilePath, "Real content");
        
        try
        {
            var linkPath = Path.Combine(folderPath, "link_file.txt");
            if (OperatingSystem.IsWindows())
            {
                File.CreateSymbolicLink(linkPath, realFilePath);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, realFilePath);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.True(result.EndsWith(" B") || result.EndsWith(" KB"));
    }

    [Fact]
    public void GetFolderSizeLabel_VeryLargeFile_ReturnsTBFormat()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        
        var folderPath = Path.Combine(_tempDirectory, "tb_folder");
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, "huge_file.txt");
        
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Create);
            fileStream.SetLength(2L * 1024 * 1024 * 1024 * 1024);
            
            var result = _fileSizeService.GetFolderSizeLabel(folderPath);
            
            Assert.Equal("2.00 TB", result);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void GetFolderSizeLabel_ReadOnlyFiles_ReturnsCorrectSize()
    {
        var folderPath = Path.Combine(_tempDirectory, "readonly_folder");
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, "readonly_file.txt");
        
        File.WriteAllText(filePath, "Read only content");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal("17 B", result);
        
        File.SetAttributes(filePath, FileAttributes.Normal);
    }

    [Fact]
    public void GetFolderSizeLabel_HiddenFiles_IncludesInSize()
    {
        var folderPath = Path.Combine(_tempDirectory, "hidden_folder");
        Directory.CreateDirectory(folderPath);
        var hiddenFilePath = Path.Combine(folderPath, "hidden_file.txt");
        
        File.WriteAllText(hiddenFilePath, "Hidden content");
        File.SetAttributes(hiddenFilePath, FileAttributes.Hidden);
        
        var result = _fileSizeService.GetFolderSizeLabel(folderPath);
        
        Assert.Equal("14 B", result);
        
        File.SetAttributes(hiddenFilePath, FileAttributes.Normal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                var files = Directory.GetFiles(_tempDirectory, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
                
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
            }
        }
    }
}