namespace ShaderCacheCleaner.Helpers;

public static class PathSafety
{
    public static bool IsSymlinkOrJunction(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path))
                return false;

            var attrs = File.GetAttributes(path);
            return attrs.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    public static bool IsAllowedCachePath(string path, IReadOnlyCollection<string> allowedRoots)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string rootFull;
            try
            {
                rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                continue;
            }

            if (full.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
                return true;

            if (full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
