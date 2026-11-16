using System.Diagnostics;

namespace ShaderCacheCleaner.Services;

public sealed class ProcessManager
{
    private readonly CleanupLogger _logger;

    public ProcessManager(CleanupLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> GetRunningTargets()
    {
        string[] names = ["cs2", "steam", "steamservice", "steamwebhelper"];
        var running = new List<string>();
        foreach (var name in names)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0 && !running.Contains(name, StringComparer.OrdinalIgnoreCase))
                    running.Add(name);
            }
            catch
            {
                // ignore
            }
        }
        return running;
    }

    public async Task CloseTargetsAsync(CancellationToken cancellationToken = default)
    {
        string[] names = ["cs2", "steam", "steamwebhelper", "steamservice"];
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    _logger.Info($"Fermeture du processus {name} (PID {process.Id})...");
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Impossible de fermer {name}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
