namespace ShaderCacheCleaner.Models;

public sealed class CleanupProgress
{
    public string CurrentStep { get; set; } = string.Empty;
    public string CurrentFolder { get; set; } = string.Empty;
    public int FilesDeleted { get; set; }
    public long BytesDeleted { get; set; }
    public double Percent { get; set; }
    public int ErrorCount { get; set; }
}
