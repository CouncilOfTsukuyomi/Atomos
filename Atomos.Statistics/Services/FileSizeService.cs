using CommonLib.Interfaces;

namespace Atomos.Statistics.Services;

public class FileSizeService : IFileSizeService
{
    /// <summary>
    /// Calculates the total size (in bytes) of a folder, recursively.
    /// </summary>
    /// <param name="folderPath">Path to the folder.</param>
    /// <returns>Total folder size in bytes.</returns>
    private long CalculateFolderSizeInBytes(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return 0;
        }

        var folderInfo = new DirectoryInfo(folderPath);
        var files = folderInfo.GetFiles("*", SearchOption.AllDirectories);

        return files.Sum(file => file.Length);
    }

    /// <summary>
    /// Returns a user-friendly size label (e.g., "1.23 MB") for a given folder path.
    /// </summary>
    /// <param name="folderPath">The path of the folder to measure.</param>
    /// <returns>A string representing the size as KB, MB, GB, etc.</returns>
    public string GetFolderSizeLabel(string folderPath)
    {
        var sizeInBytes = CalculateFolderSizeInBytes(folderPath);
        return FormatFileSize(sizeInBytes);
    }

    /// <summary>
    /// Converts the given size in bytes into a human-readable string (e.g., 1024 -> "1.00 KB").
    /// </summary>
    /// <param name="bytes">Size in bytes.</param>
    /// <returns>A string representation with the appropriate unit.</returns>
    private string FormatFileSize(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;
        const long tb = gb * 1024;

        return bytes switch
        {
            < kb => $"{bytes} B",
            < mb => $"{(double) bytes / kb:0.00} KB",
            < gb => $"{(double) bytes / mb:0.00} MB",
            < tb => $"{(double) bytes / gb:0.00} GB",
            _ => $"{(double) bytes / tb:0.00} TB"
        };
    }
}