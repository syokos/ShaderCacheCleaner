namespace ShaderCacheCleaner.Models;

public sealed class SystemInfoModel
{
    public string GpuName { get; set; } = "Non détecté";
    public string NvidiaDriverVersion { get; set; } = "N/A";
    public string WindowsVersion { get; set; } = "N/A";
    public string ShaderCacheState { get; set; } = "Inconnu";
    public bool SteamDetected { get; set; }
    public string SteamPath { get; set; } = string.Empty;
    public bool Cs2Detected { get; set; }
    public int SteamLibraryCount { get; set; }
    public bool IsNvidiaGpu { get; set; }
}
