namespace ShaderCacheCleaner.Models;

public sealed class CacheStatistics
{
    public long NvidiaBytes { get; set; }
    public long SteamCs2Bytes { get; set; }
    public long SteamOtherBytes { get; set; }
    public long DirectXBytes { get; set; }
    public long TotalBytes { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public List<CacheEntry> Entries { get; set; } = [];

    public CacheStatistics Clone() => new()
    {
        NvidiaBytes = NvidiaBytes,
        SteamCs2Bytes = SteamCs2Bytes,
        SteamOtherBytes = SteamOtherBytes,
        DirectXBytes = DirectXBytes,
        TotalBytes = TotalBytes,
        CapturedAt = CapturedAt,
        Entries = Entries.Select(e => new CacheEntry
        {
            Path = e.Path,
            Category = e.Category,
            DisplayName = e.DisplayName,
            SizeBytes = e.SizeBytes,
            Exists = e.Exists
        }).ToList()
    };
}
