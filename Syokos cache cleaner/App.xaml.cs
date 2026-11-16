using System.Threading;
using System.Windows;
using ShaderCacheCleaner.Helpers;
using ShaderCacheCleaner.Services;
using ShaderCacheCleaner.ViewModels;
using ShaderCacheCleaner.Views;

namespace ShaderCacheCleaner;

public partial class App : Application
{
    private static Mutex? _mutex;
    public static MainViewModel? ViewModel { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Thème sombre global avant toute fenêtre
        var dark = DarkTitleBar.WindowBackgroundBrush;
        Resources["AppStartupBackground"] = dark;

        base.OnStartup(e);
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        const string mutexName = "Global\\ShaderCacheCleaner_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            DarkDialog.Show(null,
                "Shader Cache Cleaner est déjà en cours d'exécution.",
                "Shader Cache Cleaner");
            Shutdown();
            return;
        }

        bool resume = e.Args.Any(a => string.Equals(a, "--resume", StringComparison.OrdinalIgnoreCase));

        var logger = new CleanupLogger();
        var settings = new SettingsManager();
        var steam = new SteamManager(logger);
        var directX = new DirectXCacheManager();
        var nvidia = new NvidiaManager(logger);
        var scanner = new CacheScanner(logger, steam, directX);
        var cleaner = new ShaderCacheCleanerService(logger, scanner);
        var systemInfo = new SystemInfoService(logger, steam, nvidia);
        var processes = new ProcessManager(logger);
        var recovery = new StartupRecoveryManager(logger, settings);
        var reboot = new SystemRebootManager(logger, recovery);
        var icons = new IconManager();

        ViewModel = new MainViewModel(
            logger, settings, scanner, cleaner, nvidia, systemInfo,
            processes, recovery, reboot, icons, resume);

        var window = new MainWindow(ViewModel)
        {
            Background = DarkTitleBar.WindowBackgroundBrush,
            // Invisible jusqu'au premier rendu sombre (évite le flash blanc post-reboot)
            Opacity = 0
        };

        MainWindow = window;

        void Reveal()
        {
            DarkTitleBar.ApplyImmediate(window);
            window.Opacity = 1;
        }

        window.SourceInitialized += (_, _) => DarkTitleBar.ApplyImmediate(window);
        window.ContentRendered += (_, _) => Reveal();

        window.Show();
        window.Activate();

        // Filet de sécurité si ContentRendered est déjà passé
        if (window.IsLoaded)
            Reveal();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        _mutex?.Dispose();
    }
}
