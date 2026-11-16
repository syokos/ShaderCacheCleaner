using System.Windows;
using System.Windows.Media.Imaging;

namespace ShaderCacheCleaner.Services;

public sealed class IconManager
{
    public BitmapImage? LoadApplicationIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public void ApplyToWindow(Window window)
    {
        var icon = LoadApplicationIcon();
        if (icon is not null)
            window.Icon = icon;
    }

    public void CreateDesktopShortcut(string exePath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "Shader Cache Cleaner.lnk");
        var iconPath = exePath;

        // WScript COM for .lnk without extra NuGet dependency
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            throw new InvalidOperationException("Impossible de créer le raccourci (WScript.Shell indisponible).");

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = exePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
        shortcut.IconLocation = iconPath + ",0";
        shortcut.Description = "Shader Cache Cleaner — Gaming Shader Maintenance";
        shortcut.Save();
    }
}
