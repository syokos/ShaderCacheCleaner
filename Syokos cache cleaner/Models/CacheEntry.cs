namespace ShaderCacheCleaner.Models;

public sealed class CacheEntry
{
    public required string Path { get; init; }
    public required CacheCategory Category { get; init; }
    public required string DisplayName { get; init; }
    public long SizeBytes { get; set; }
    public bool Exists { get; set; }
}
