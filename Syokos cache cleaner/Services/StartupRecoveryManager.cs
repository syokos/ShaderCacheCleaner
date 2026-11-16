using System.Diagnostics;
using ShaderCacheCleaner.Models;

namespace ShaderCacheCleaner.Services;

public sealed class StartupRecoveryManager
{
    public const string TaskName = "ShaderCacheCleanerResume";
    private readonly CleanupLogger _logger;
    private readonly SettingsManager _settings;

    public StartupRecoveryManager(CleanupLogger logger, SettingsManager settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public SessionState? TryLoadPendingSession()
    {
        var session = _settings.LoadSession();
        if (session is null)
            return null;

        if (session.Phase is CleanupPhase.Idle or CleanupPhase.Completed or CleanupPhase.Failed)
            return session;

        return session;
    }

    public void RegisterResumeTask(string exePath)
    {
        // Remove previous task if any
        UnregisterResumeTask();

        var args = $"/Create /F /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --resume\" /SC ONLOGON /RL HIGHEST /RU \"{Environment.UserName}\"";
        RunSchtasks(args);
        _logger.Info($"Planificateur : tâche {TaskName} créée.");
    }

    public void UnregisterResumeTask()
    {
        RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
    }

    public void MarkPhase(SessionState state, CleanupPhase phase)
    {
        state.Phase = phase;
        state.UpdatedAt = DateTime.UtcNow;
        _settings.SaveSession(state);
        _logger.Info($"État persisté : {phase}");
    }

    private void RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit(15000);
        }
        catch (Exception ex)
        {
            _logger.Warn($"schtasks : {ex.Message}");
        }
    }
}
