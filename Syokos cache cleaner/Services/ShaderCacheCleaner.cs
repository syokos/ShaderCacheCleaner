namespace ShaderCacheCleaner.Services;

/// <summary>
/// Point d'entrée sémantique demandé par l'architecture.
/// Délègue au service de nettoyage principal.
/// </summary>
public sealed class ShaderCacheCleaner
{
    private readonly ShaderCacheCleanerService _inner;

    public ShaderCacheCleaner(ShaderCacheCleanerService inner)
    {
        _inner = inner;
    }

    public ShaderCacheCleanerService Service => _inner;
}
