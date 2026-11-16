using System.Management;
using ShaderCacheCleaner.Helpers;
using ShaderCacheCleaner.Models;

namespace ShaderCacheCleaner.Services;

public sealed class SystemInfoService
{
    private readonly CleanupLogger _logger;
    private readonly SteamManager _steam;
    private readonly NvidiaManager _nvidia;

    public SystemInfoService(CleanupLogger logger, SteamManager steam, NvidiaManager nvidia)
    {
        _logger = logger;
        _steam = steam;
        _nvidia = nvidia;
    }

    public SystemInfoModel Collect()
    {
        var info = new SystemInfoModel
        {
            WindowsVersion = WindowsVersionHelper.GetDisplayVersion(),
            GpuName = DetectGpu(out var isNvidia),
            IsNvidiaGpu = isNvidia
        };

        if (isNvidia)
        {
            info.NvidiaDriverVersion = _nvidia.GetDriverVersion() ?? FormatWmiDriver(DetectDriverFromWmi()) ?? "Inconnue";
            info.ShaderCacheState = string.Empty; // non affiché dans l'UI
        }
        else
        {
            info.NvidiaDriverVersion = "N/A";
            info.ShaderCacheState = string.Empty;
        }

        _steam.Detect();
        info.SteamDetected = _steam.IsInstalled;
        info.SteamPath = _steam.SteamRoot ?? string.Empty;
        info.Cs2Detected = _steam.IsCs2Installed;
        info.SteamLibraryCount = _steam.LibraryRoots.Count;

        _logger.Info($"Système : {info.WindowsVersion} | GPU : {info.GpuName} | Pilote : {info.NvidiaDriverVersion}");
        return info;
    }

    private static string? FormatWmiDriver(string? wmi)
    {
        if (string.IsNullOrWhiteSpace(wmi))
            return null;

        // WMI souvent "32.0.15.xxxx" — on conserve tel quel, sans branche R.
        return wmi.Trim();
    }

    private string DetectGpu(out bool isNvidia)
    {
        isNvidia = false;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? string.Empty;
                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    isNvidia = true;
                    return name;
                }
            }

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Détection GPU : {ex.Message}");
        }

        return "Non détecté";
    }

    private static string? DetectDriverFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DriverVersion, Name FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? string.Empty;
                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    return obj["DriverVersion"]?.ToString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
