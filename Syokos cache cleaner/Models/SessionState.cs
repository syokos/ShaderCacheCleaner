namespace ShaderCacheCleaner.Models;

public sealed class SessionState
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public CleanupPhase Phase { get; set; } = CleanupPhase.Idle;
    public CleanupMode Mode { get; set; } = CleanupMode.Cs2Clean;
    public bool SimulationMode { get; set; }
    public bool CreateDesktopShortcut { get; set; }
    public CacheStatistics? BeforeStats { get; set; }
    public CacheStatistics? AfterStats { get; set; }
    public CacheStatistics? PostCleanStats { get; set; }
    public NvidiaBackup? NvidiaBackup { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int FilesDeleted { get; set; }
    public long BytesDeleted { get; set; }
    public int ErrorCount { get; set; }
    public bool PartialRecreationDetected { get; set; }
    public bool CleanupHadCriticalFailures { get; set; }
    public int FirstRebootCount { get; set; }
    public int SecondRebootCount { get; set; }
    public bool BeforeStatsLocked { get; set; }
}

public sealed class NvidiaBackup
{
    public bool WasAvailable { get; set; }
    public string OriginalSettingLabel { get; set; } = "Unknown";
    public uint? OriginalSettingValue { get; set; }
    public bool DisabledSuccessfully { get; set; }
    public bool RestoredSuccessfully { get; set; }
    public bool UsedManualAssistedFlow { get; set; }
    public string Notes { get; set; } = string.Empty;
}
