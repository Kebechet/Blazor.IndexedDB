using Microsoft.Extensions.DependencyInjection;

namespace Kebechet.Blazor.IndexedDB;

/// <summary>Registration helpers for <see cref="IIndexedDbStorage"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IIndexedDbStorage"/>. Scoped rather than singleton because the service caches
    /// <c>IJSObjectReference</c>s, which belong to one circuit - a singleton would hand a torn-down circuit's
    /// module to the next user on Blazor Server. The options are a singleton snapshot of what
    /// <paramref name="configure"/> produced.
    /// </summary>
    public static IServiceCollection AddIndexedDbStorage(this IServiceCollection services, Action<IndexedDbStorageOptions>? configure = null)
    {
        var options = new IndexedDbStorageOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddScoped<IIndexedDbStorage, IndexedDbStorage>();
        return services;
    }
}
