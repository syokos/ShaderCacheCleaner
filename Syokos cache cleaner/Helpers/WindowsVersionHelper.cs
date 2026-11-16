using Microsoft.Win32;

namespace ShaderCacheCleaner.Helpers;

public static class WindowsVersionHelper
{
    public static string GetDisplayVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null)
                return Fallback();

            var productName = key.GetValue("ProductName") as string ?? "Windows";
            var displayVersion = key.GetValue("DisplayVersion") as string
                                 ?? key.GetValue("ReleaseId") as string
                                 ?? string.Empty;
            var build = key.GetValue("CurrentBuildNumber") as string
                        ?? key.GetValue("CurrentBuild") as string
                        ?? "0";
            var ubr = key.GetValue("UBR")?.ToString() ?? "0";

            // Windows 11 reporté comme "Windows 10" dans ProductName sur beaucoup d'installations.
            if (int.TryParse(build, out var buildNum) && buildNum >= 22000)
                productName = "Windows 11";
            else if (productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) &&
                     !productName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
                productName = "Windows 10";

            var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            var edition = displayVersion.Length > 0 ? $" {displayVersion}" : string.Empty;
            return $"{productName}{edition} (build {build}.{ubr}, {arch})";
        }
        catch
        {
            return Fallback();
        }
    }

    private static string Fallback()
    {
        var v = Environment.OSVersion.Version;
        var name = v.Build >= 22000 ? "Windows 11" : "Windows";
        return $"{name} build {v.Build}";
    }
}
