using ShaderCacheCleaner.Helpers;
using ShaderCacheCleaner.Models;

namespace ShaderCacheCleaner.Services;

public sealed class CacheScanner
{
    private readonly CleanupLogger _logger;
    private readonly SteamManager _steam;
    private readonly DirectXCacheManager _directX;

    public CacheScanner(CleanupLogger logger, SteamManager steam, DirectXCacheManager directX)
    {
        _logger = logger;
        _steam = steam;
        _directX = directX;
    }

    public IReadOnlyList<string> GetNvidiaCachePaths(bool fullClean)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var paths = new List<string>
        {
            Path.Combine(local, "NVIDIA", "DXCache"),
            Path.Combine(local, "NVIDIA", "GLCache"),
            Path.Combine(local, "NVIDIA Corporation", "NV_Cache")
        };

        if (fullClean)
        {
            paths.Add(Path.Combine(local, "NVIDIA", "OptixCache"));
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA Corporation", "NV_Cache"));

            var tempNvidia = Path.Combine(Path.GetTempPath(), "NVIDIA Corporation");
            if (Directory.Exists(tempNvidia))
            {
                foreach (var dir in Directory.EnumerateDirectories(tempNvidia, "*Cache*", SearchOption.TopDirectoryOnly))
                    paths.Add(dir);
            }
        }

        return paths
            .Select(p =>
            {
                try { return Path.GetFullPath(p); }
                catch { return p; }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Cibles de nettoyage selon le mode (inchangé : CS2 = NVIDIA+CS2+DX, Full = +Steam autres).
    /// </summary>
    public List<CacheEntry> BuildTargets(CleanupMode mode) =>
        BuildTargetsCore(mode, includeSteamOthersForDisplay: mode == CleanupMode.FullClean);

    /// <summary>
    /// Cibles de mesure UI : toujours inclure CS2 + Steam (autres jeux) pour afficher les vraies tailles,
    /// sans élargir le périmètre de nettoyage (CleanAsync utilise BuildTargets).
    /// </summary>
    public List<CacheEntry> BuildScanTargets(CleanupMode mode) =>
        BuildTargetsCore(mode, includeSteamOthersForDisplay: true);

    private List<CacheEntry> BuildTargetsCore(CleanupMode mode, bool includeSteamOthersForDisplay)
    {
        _steam.Detect();
        var full = mode == CleanupMode.FullClean;
        var entries = new List<CacheEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path, CacheCategory category, string displayName)
        {
            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch { return; }

            if (!seen.Add(fullPath))
                return;

            // Un dossier compte seulement s'il existe ; la taille réelle est calculée au scan.
            entries.Add(new CacheEntry
            {
                Path = fullPath,
                Category = category,
                DisplayName = displayName,
                Exists = Directory.Exists(fullPath),
                SizeBytes = 0
            });
        }

        foreach (var path in GetNvidiaCachePaths(full))
            Add(path, CacheCategory.Nvidia, "Cache NVIDIA");

        foreach (var path in _steam.GetCs2ShaderCachePaths())
            Add(path, CacheCategory.SteamCs2, "Steam CS2 shadercache");

        if (includeSteamOthersForDisplay)
        {
            foreach (var path in _steam.GetOtherShaderCachePaths())
                Add(path, CacheCategory.SteamOther, "Steam shadercache");
        }

        foreach (var path in _directX.GetCachePaths())
            Add(path, CacheCategory.DirectX, "DirectX D3DSCache");

        return entries;
    }

    public CacheStatistics Scan(CleanupMode mode, CancellationToken cancellationToken = default)
    {
        var entries = BuildScanTargets(mode);
        long nvidia = 0, cs2 = 0, other = 0, dx = 0;
        var countedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.Exists)
                continue;

            bool isSteamCache = entry.Category is CacheCategory.SteamCs2 or CacheCategory.SteamOther;

            // NVIDIA / DirectX : ignorer les jonctions sortantes (comportement historique).
            // Steam / CS2 : autoriser les jonctions de bibliothèques Steam (fréquentes).
            if (!isSteamCache && PathSafety.IsSymlinkOrJunction(entry.Path))
            {
                _logger.Warn($"Lien symbolique/jonction ignoré : {entry.Path}");
                entry.Exists = false;
                continue;
            }

            entry.SizeBytes = CalculateDirectorySize(
                entry.Path,
                cancellationToken,
                followSteamJunctions: isSteamCache,
                countedFiles: countedFiles);

            // Dossier existant mais vide = 0 Mo (après vérification réelle)
            switch (entry.Category)
            {
                case CacheCategory.Nvidia: nvidia += entry.SizeBytes; break;
                case CacheCategory.SteamCs2: cs2 += entry.SizeBytes; break;
                case CacheCategory.SteamOther: other += entry.SizeBytes; break;
                case CacheCategory.DirectX: dx += entry.SizeBytes; break;
            }
        }

        var stats = new CacheStatistics
        {
            Entries = entries,
            NvidiaBytes = nvidia,
            SteamCs2Bytes = cs2,
            SteamOtherBytes = other,
            DirectXBytes = dx,
            TotalBytes = nvidia + cs2 + other + dx,
            CapturedAt = DateTime.UtcNow
        };

        _logger.Info($"Scan terminé : total={ByteFormatter.FormatFr(stats.TotalBytes)} (NVIDIA={ByteFormatter.FormatFr(nvidia)}, CS2={ByteFormatter.FormatFr(cs2)}, Steam={ByteFormatter.FormatFr(other)}, DX={ByteFormatter.FormatFr(dx)})");
        return stats;
    }

    public static long CalculateDirectorySize(
        string path,
        CancellationToken cancellationToken = default,
        bool followSteamJunctions = false,
        HashSet<string>? countedFiles = null)
    {
        long total = 0;
        try
        {
            if (!Directory.Exists(path))
                return 0;

            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = stack.Pop();

                // Racine autorisée même si jonction Steam ; sous-jonctions hors arbre : skip sauf Steam
                if (PathSafety.IsSymlinkOrJunction(current))
                {
                    if (!followSteamJunctions && !current.Equals(path, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!followSteamJunctions && current.Equals(path, StringComparison.OrdinalIgnoreCase))
                        return 0;
                }

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(current); }
                catch { continue; }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (PathSafety.IsSymlinkOrJunction(file))
                            continue;

                        string fullFile;
                        try { fullFile = Path.GetFullPath(file); }
                        catch { continue; }

                        if (countedFiles is not null && !countedFiles.Add(fullFile))
                            continue;

                        total += new FileInfo(fullFile).Length;
                    }
                    catch
                    {
                        // fichier verrouillé / inaccessible
                    }
                }

                IEnumerable<string> dirs;
                try { dirs = Directory.EnumerateDirectories(current); }
                catch { continue; }

                foreach (var dir in dirs)
                {
                    if (PathSafety.IsSymlinkOrJunction(dir) && !followSteamJunctions)
                        continue;
                    stack.Push(dir);
                }
            }
        }
        catch
        {
            // ignore root errors
        }

        return total;
    }
}
