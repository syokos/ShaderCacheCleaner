namespace ShaderCacheCleaner.Models;

public enum CleanupPhase
{
    Idle,
    Preparing,
    NvidiaDisabled,
    WaitingForFirstReboot,
    Cleaning,
    RestoringNvidia,
    WaitingForSecondReboot,
    FinalScan,
    Completed,
    Failed
}
