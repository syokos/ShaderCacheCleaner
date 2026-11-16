using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ShaderCacheCleaner.Services;

public sealed class SteamManager
{
    private readonly CleanupLogger _logger;

    public SteamManager(CleanupLogger logger)
    {
        _logger = logger;
    }

    public string? SteamRoot { get; private set; }
    public IReadOnlyList<string> LibraryRoots { get; private set; } = [];
    public bool IsInstalled => !string.IsNullOrEmpty(SteamRoot) && Directory.Exists(SteamRoot);
    public bool IsCs2Installed { get; private set; }

    public void Detect()
    {
        SteamRoot = FindSteamRoot();
        if (SteamRoot is null)
        {
            // Dernier recours : bibliothèques trouvées via scan disque
            LibraryRoots = DiscoverLibrariesByScanning().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            IsCs2Installed = DetectCs2(LibraryRoots);
            if (LibraryRoots.Count == 0)
            {
                _logger.Warn("Installation Steam non détectée.");
                return;
            }

            SteamRoot = LibraryRoots[0];
            _logger.Info($"Steam (scan disque) : {LibraryRoots.Count} bibliothèque(s) | CS2 : {(IsCs2Installed ? "oui" : "non")}");
            return;
        }

        _logger.Info($"Steam détecté : {SteamRoot}");
        var libraries = DiscoverLibraries(SteamRoot);
        libraries.AddRange(DiscoverLibrariesByScanning());
        LibraryRoots = libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IsCs2Installed = DetectCs2(LibraryRoots);
        _logger.Info($"Bibliothèques Steam : {LibraryRoots.Count} | CS2 : {(IsCs2Installed ? "oui" : "non")}");
        LogShaderCachePresence();
    }

    public IEnumerable<string> GetCs2ShaderCachePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lib in LibraryRoots)
        {
            foreach (var path in EnumerateCs2CacheCandidates(lib))
            {
                if (seen.Add(path))
                    yield return path;
            }
        }

        // Filet de sécurité : chemins connus même si la bibliothèque n'a pas été listée
        if (SteamRoot is not null)
        {
            foreach (var path in EnumerateCs2CacheCandidates(SteamRoot))
            {
                if (seen.Add(path))
                    yield return path;
            }
        }
    }

    public IEnumerable<string> GetAllShaderCachePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> libraryCandidates()
        {
            foreach (var lib in LibraryRoots)
                yield return lib;
            if (SteamRoot is not null)
                yield return SteamRoot;
        }

        foreach (var lib in libraryCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var root = Path.Combine(lib, "steamapps", "shadercache");
            if (!Directory.Exists(root))
                continue;

            foreach (var dir in SafeEnumerateDirectories(root))
            {
                var full = NormalizePath(dir);
                if (full is not null && seen.Add(full))
                    yield return full;
            }
        }
    }

    public IEnumerable<string> GetOtherShaderCachePaths()
    {
        foreach (var path in GetAllShaderCachePaths())
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.Equals(name, "730", StringComparison.OrdinalIgnoreCase))
                yield return path;
        }
    }

    private IEnumerable<string> EnumerateCs2CacheCandidates(string libraryRoot)
    {
        // Emplacement principal Steam Shader Pre-Caching (AppID 730)
        yield return Path.Combine(libraryRoot, "steamapps", "shadercache", "730");

        // Variantes éventuelles sous shadercache (sous-dossiers nommés 730*)
        var shaderRoot = Path.Combine(libraryRoot, "steamapps", "shadercache");
        if (Directory.Exists(shaderRoot))
        {
            foreach (var dir in SafeEnumerateDirectories(shaderRoot))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "730", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("730_", StringComparison.OrdinalIgnoreCase))
                {
                    yield return dir;
                }
            }
        }

        // Si CS2 est installé dans cette bibliothèque, certains dépôts créent aussi
        // des caches nommés autrement : on ne les ajoute pas ici (évite les faux positifs).
        // On couvre en revanche le dossier shadercache racine 730 via appmanifest.
        var manifest = Path.Combine(libraryRoot, "steamapps", "appmanifest_730.acf");
        if (File.Exists(manifest))
            yield return Path.Combine(libraryRoot, "steamapps", "shadercache", "730");
    }

    private void LogShaderCachePresence()
    {
        var cs2 = GetCs2ShaderCachePaths().ToList();
        _logger.Info($"Caches CS2 candidats : {cs2.Count}");
        foreach (var p in cs2)
        {
            var exists = Directory.Exists(p);
            long size = 0;
            if (exists)
            {
                try { size = CacheScanner.CalculateDirectorySize(p, followSteamJunctions: true); }
                catch { /* ignore */ }
            }

            _logger.Info($"  {(exists ? "OK" : "absent")} {p} → {Helpers.ByteFormatter.FormatFr(size)}");
        }
    }

    private string? FindSteamRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var normalized = NormalizePath(path);
                if (normalized is not null && Directory.Exists(normalized))
                    return normalized;
            }

            var steamExe = key?.GetValue("SteamExe") as string;
            if (!string.IsNullOrWhiteSpace(steamExe))
            {
                var dir = Path.GetDirectoryName(steamExe.Replace('/', '\\'));
                var normalized = NormalizePath(dir);
                if (normalized is not null && Directory.Exists(normalized))
                    return normalized;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Lecture registre Steam : {ex.Message}");
        }

        try
        {
            using var key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                              ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var path = key32?.GetValue("InstallPath") as string;
            var normalized = NormalizePath(path);
            if (normalized is not null && Directory.Exists(normalized))
                return normalized;
        }
        catch
        {
            // ignore
        }

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            @"D:\Steam",
            @"D:\SteamLibrary",
            @"E:\Steam",
            @"E:\SteamLibrary",
            @"F:\Steam",
            @"F:\SteamLibrary",
            @"G:\Steam",
            @"G:\SteamLibrary"
        ];

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) &&
                (File.Exists(Path.Combine(candidate, "steam.exe")) ||
                 Directory.Exists(Path.Combine(candidate, "steamapps"))))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private List<string> DiscoverLibraries(string steamRoot)
    {
        var libraries = new List<string>();
        var rootNorm = NormalizePath(steamRoot);
        if (rootNorm is not null)
            libraries.Add(rootNorm);

        var vdfCandidates = new[]
        {
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamRoot, "config", "libraryfolders.vdf")
        };

        foreach (var vdfPath in vdfCandidates)
        {
            if (!File.Exists(vdfPath))
                continue;

            try
            {
                var content = File.ReadAllText(vdfPath);
                // "path" "X:\..."  ou  "path"\t\t"X:\\..."
                var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    var raw = UnescapeVdfPath(match.Groups[1].Value);
                    var normalized = NormalizePath(raw);
                    if (normalized is not null && Directory.Exists(normalized))
                        libraries.Add(normalized);
                }

                _logger.Info($"libraryfolders.vdf lu : {vdfPath} ({matches.Count} chemin(s))");
            }
            catch (Exception ex)
            {
                _logger.Error($"Analyse {vdfPath} : {ex.Message}");
            }
        }

        if (libraries.Count == 1)
            _logger.Warn("Une seule bibliothèque Steam listée — vérification scan disque en complément.");

        return libraries;
    }

    private List<string> DiscoverLibrariesByScanning()
    {
        var found = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                    continue;

                // Chemins fréquents uniquement (rapide, pas de scan récursif complet)
                string[] guesses =
                [
                    Path.Combine(drive.Name, "Steam"),
                    Path.Combine(drive.Name, "SteamLibrary"),
                    Path.Combine(drive.Name, "Games", "Steam"),
                    Path.Combine(drive.Name, "Games", "SteamLibrary"),
                    Path.Combine(drive.Name, "Program Files (x86)", "Steam"),
                    Path.Combine(drive.Name, "Program Files", "Steam")
                ];

                foreach (var guess in guesses)
                {
                    var steamApps = Path.Combine(guess, "steamapps");
                    if (Directory.Exists(steamApps))
                    {
                        var norm = NormalizePath(guess);
                        if (norm is not null)
                            found.Add(norm);
                    }

                    // Cas : le dossier est déjà une racine steamapps (rare)
                    if (Directory.Exists(Path.Combine(guess, "shadercache")) &&
                        string.Equals(Path.GetFileName(guess), "steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        var parent = Directory.GetParent(guess)?.FullName;
                        var norm = NormalizePath(parent);
                        if (norm is not null)
                            found.Add(norm);
                    }
                }

                // Recherche ciblée : shadercache\730 ou appmanifest_730 (1 niveau sous la racine du disque)
                try
                {
                    foreach (var top in Directory.EnumerateDirectories(drive.Name))
                    {
                        var steamApps = Path.Combine(top, "steamapps");
                        var hitCache = Path.Combine(steamApps, "shadercache", "730");
                        var hitManifest = Path.Combine(steamApps, "appmanifest_730.acf");
                        if (!Directory.Exists(hitCache) && !File.Exists(hitManifest))
                            continue;
                        var norm = NormalizePath(top);
                        if (norm is not null)
                            found.Add(norm);
                    }
                }
                catch
                {
                    // permissions
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Scan disque Steam : {ex.Message}");
        }

        return found;
    }

    private static bool DetectCs2(IReadOnlyList<string> libraries)
    {
        foreach (var lib in libraries)
        {
            if (File.Exists(Path.Combine(lib, "steamapps", "appmanifest_730.acf")))
                return true;

            if (Directory.Exists(Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive")))
                return true;

            if (Directory.Exists(Path.Combine(lib, "steamapps", "shadercache", "730")))
                return true;
        }

        return false;
    }

    private static string UnescapeVdfPath(string raw) =>
        raw.Replace("\\\\", "\\").Replace('/', '\\').Trim();

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.GetFullPath(UnescapeVdfPath(path))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return [];
        }
    }
}
