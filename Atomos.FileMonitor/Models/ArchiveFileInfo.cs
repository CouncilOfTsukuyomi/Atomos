namespace Atomos.FileMonitor.Models;

public record ArchiveFileInfo
{
    public string FileName { get; init; } = string.Empty;
    public ulong Size { get; init; }
    public string Extension { get; init; } = string.Empty;
    public bool IsModFile { get; init; }
    public bool IsPreDt { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public DateTime? LastModified { get; init; }
}