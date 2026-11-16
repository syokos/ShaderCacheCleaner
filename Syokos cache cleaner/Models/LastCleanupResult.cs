namespace ShaderCacheCleaner.Models;

public sealed class LastCleanupResult
{
    public CleanupMode Mode { get; set; }
    public long BeforeBytes { get; set; }
    public long AfterBytes { get; set; }
    public long FreedBytes { get; set; }
    public double ReductionPercent { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public bool Success { get; set; }
}
