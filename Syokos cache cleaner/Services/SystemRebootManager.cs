using System.Diagnostics;

namespace ShaderCacheCleaner.Services;

public sealed class SystemRebootManager
{
    private readonly CleanupLogger _logger;
    private readonly StartupRecoveryManager _recovery;

    public SystemRebootManager(CleanupLogger logger, StartupRecoveryManager recovery)
    {
        _logger = logger;
        _recovery = recovery;
    }

    public void PrepareAutoStart(string exePath)
    {
        _recovery.RegisterResumeTask(exePath);
        _logger.Info("Tâche de reprise après redémarrage enregistrée.");
    }

    public void CancelPendingReboot()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/a",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.Dispose();
            _logger.Info("Redémarrage annulé.");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Annulation redémarrage : {ex.Message}");
        }
    }

    public void RequestReboot(int delaySeconds, string message)
    {
        delaySeconds = Math.Clamp(delaySeconds, 5, 600);
        var args = $"/r /t {delaySeconds} /c \"{message.Replace("\"", "'")}\"";
        _logger.Info($"Redémarrage demandé dans {delaySeconds}s.");
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        })?.Dispose();
    }
}
