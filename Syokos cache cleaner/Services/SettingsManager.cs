using System.Text.Json;
using ShaderCacheCleaner.Models;

namespace ShaderCacheCleaner.Services;

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string AppDataDirectory { get; }
    public string SessionStatePath { get; }
    public string BeforeSnapshotPath { get; }
    public string LastResultPath { get; }

    public SettingsManager()
    {
        AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShaderCacheCleaner");
        Directory.CreateDirectory(AppDataDirectory);
        SessionStatePath = Path.Combine(AppDataDirectory, "session-state.json");
        BeforeSnapshotPath = Path.Combine(AppDataDirectory, "before-stats.json");
        LastResultPath = Path.Combine(AppDataDirectory, "last-result.json");
    }

    public void SaveSession(SessionState state)
    {
        state.UpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(SessionStatePath, json);

        if (state.BeforeStats is not null && state.BeforeStats.TotalBytes >= 0)
            SaveBeforeStats(state.BeforeStats);
    }

    public SessionState? LoadSession()
    {
        if (!File.Exists(SessionStatePath))
            return null;

        try
        {
            var json = File.ReadAllText(SessionStatePath);
            return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void SaveBeforeStats(CacheStatistics stats)
    {
        var json = JsonSerializer.Serialize(stats, JsonOptions);
        File.WriteAllText(BeforeSnapshotPath, json);
    }

    public CacheStatistics? LoadBeforeStats()
    {
        if (!File.Exists(BeforeSnapshotPath))
            return null;

        try
        {
            var json = File.ReadAllText(BeforeSnapshotPath);
            return JsonSerializer.Deserialize<CacheStatistics>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void SaveLastResult(LastCleanupResult result)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        File.WriteAllText(LastResultPath, json);
    }

    public LastCleanupResult? LoadLastResult()
    {
        if (!File.Exists(LastResultPath))
            return null;

        try
        {
            var json = File.ReadAllText(LastResultPath);
            return JsonSerializer.Deserialize<LastCleanupResult>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Efface la session en cours, conserve le dernier bilan.</summary>
    public void ClearTemporaryData()
    {
        TryDelete(SessionStatePath);
        TryDelete(BeforeSnapshotPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
