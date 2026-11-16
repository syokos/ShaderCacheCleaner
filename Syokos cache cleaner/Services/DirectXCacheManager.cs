namespace ShaderCacheCleaner.Services;

public sealed class DirectXCacheManager
{
    public string D3DsCachePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache");

    public IEnumerable<string> GetCachePaths()
    {
        if (Directory.Exists(D3DsCachePath))
            yield return D3DsCachePath;
    }
}
