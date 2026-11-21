using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PEMS.UI.Abstractions;
using PEMS.UI.Rendering;

namespace PEMS.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the component registry, the request-free renderer and the locale service. Call once at startup. A host that needs its own
    /// <see cref="IPemsLocale"/> registers it before this call (the default is added only when none exists).
    /// </summary>
    public static IServiceCollection AddPemsUi(this IServiceCollection services, Action<PemsUiOptions>? configure = null)
    {
        services.AddLogging();
        var options = new PemsUiOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddScoped<IPemsLocale, PemsLocale>();
        services.TryAddSingleton<IPemsComponentRegistry, PemsComponentRegistry>();
        services.TryAddScoped<IPemsComponentRenderer, PemsComponentRenderer>();
        services.TryAddSingleton<PEMS.UI.Manifest.ComponentManifestCatalog>();
        return services;
    }
}
