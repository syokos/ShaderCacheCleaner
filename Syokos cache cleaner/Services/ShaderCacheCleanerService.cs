using ShaderCacheCleaner.Helpers;
using ShaderCacheCleaner.Models;

namespace ShaderCacheCleaner.Services;

public sealed class ShaderCacheCleanerService
{
    private readonly CleanupLogger _logger;
    private readonly CacheScanner _scanner;

    public ShaderCacheCleanerService(CleanupLogger logger, CacheScanner scanner)
    {
        _logger = logger;
        _scanner = scanner;
    }

    public async Task<(int filesDeleted, long bytesDeleted, int errors, bool criticalFailure)> CleanAsync(
        CleanupMode mode,
        bool simulation,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var entries = _scanner.BuildTargets(mode);
        var allowedRoots = entries.Select(e => e.Path).ToList();
        int filesDeleted = 0;
        long bytesDeleted = 0;
        int errors = 0;
        int processed = 0;
        bool criticalFailure = false;

        // Estimate work units = number of existing folders
        var existing = entries.Where(e => e.Exists).ToList();
        if (existing.Count == 0)
        {
            progress?.Report(new CleanupProgress
            {
                CurrentStep = "Aucun cache à nettoyer",
                Percent = 100
            });
            return (0, 0, 0, false);
        }

        foreach (var entry in existing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;
            var pct = processed * 100.0 / existing.Count;

            progress?.Report(new CleanupProgress
            {
                CurrentStep = simulation ? "Simulation" : "Suppression définitive",
                CurrentFolder = entry.Path,
                FilesDeleted = filesDeleted,
                BytesDeleted = bytesDeleted,
                Percent = pct,
                ErrorCount = errors
            });

            if (!PathSafety.IsAllowedCachePath(entry.Path, allowedRoots))
            {
                _logger.Error($"Chemin refusé (hors liste autorisée) : {entry.Path}");
                errors++;
                criticalFailure = true;
                continue;
            }

            if (PathSafety.IsSymlinkOrJunction(entry.Path))
            {
                _logger.Warn($"Jonction/symlink ignoré : {entry.Path}");
                errors++;
                continue;
            }

            _logger.Info($"{(simulation ? "[SIM] " : "")}Nettoyage de {entry.DisplayName} → {entry.Path}");

            var result = await Task.Run(() => CleanDirectoryContents(entry.Path, allowedRoots, simulation, cancellationToken), cancellationToken);
            filesDeleted += result.files;
            bytesDeleted += result.bytes;
            errors += result.errors;
            if (result.critical)
                criticalFailure = true;

            progress?.Report(new CleanupProgress
            {
                CurrentStep = simulation ? "Simulation" : "Suppression définitive",
                CurrentFolder = entry.Path,
                FilesDeleted = filesDeleted,
                BytesDeleted = bytesDeleted,
                Percent = pct,
                ErrorCount = errors
            });
        }

        _logger.Info($"Nettoyage terminé : {filesDeleted} fichiers, {ByteFormatter.FormatFr(bytesDeleted)}, {errors} erreurs.");
        return (filesDeleted, bytesDeleted, errors, criticalFailure);
    }

    private (int files, long bytes, int errors, bool critical) CleanDirectoryContents(
        string root,
        IReadOnlyCollection<string> allowedRoots,
        bool simulation,
        CancellationToken cancellationToken)
    {
        int files = 0;
        long bytes = 0;
        int errors = 0;
        bool critical = false;

        if (!Directory.Exists(root))
            return (0, 0, 0, false);

        // Delete files first (depth-first for directories)
        var stack = new Stack<string>();
        var allDirs = new List<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            if (!PathSafety.IsAllowedCachePath(current, allowedRoots))
            {
                _logger.Error($"Arrêt : chemin non autorisé rencontré {current}");
                return (files, bytes, errors + 1, true);
            }

            if (PathSafety.IsSymlinkOrJunction(current) && !current.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn($"Sous-jonction ignorée : {current}");
                errors++;
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!PathSafety.IsAllowedCachePath(file, allowedRoots))
                    {
                        errors++;
                        critical = true;
                        continue;
                    }

                    if (PathSafety.IsSymlinkOrJunction(file))
                    {
                        errors++;
                        continue;
                    }

                    long size = 0;
                    try { size = new FileInfo(file).Length; } catch { /* ignore */ }

                    try
                    {
                        if (!simulation)
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        files++;
                        bytes += size;
                        _logger.Info($"{(simulation ? "[SIM] " : "")}Supprimé : {file}");
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.Warn($"Fichier verrouillé/inaccessible : {file} ({ex.Message})");
                    }
                }

                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    stack.Push(dir);
                    allDirs.Add(dir);
                }
            }
            catch (Exception ex)
            {
                errors++;
                _logger.Warn($"Parcours impossible : {current} ({ex.Message})");
            }
        }

        // Remove empty subdirectories, never the authorized root itself
        foreach (var dir in allDirs.OrderByDescending(d => d.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PathSafety.IsAllowedCachePath(dir, allowedRoots))
                continue;

            if (dir.Equals(root, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (!Directory.Exists(dir))
                    continue;

                if (Directory.EnumerateFileSystemEntries(dir).Any())
                    continue;

                if (!simulation)
                    Directory.Delete(dir, recursive: false);
            }
            catch (Exception ex)
            {
                errors++;
                _logger.Warn($"Dossier non supprimé : {dir} ({ex.Message})");
            }
        }

        return (files, bytes, errors, critical);
    }
}
