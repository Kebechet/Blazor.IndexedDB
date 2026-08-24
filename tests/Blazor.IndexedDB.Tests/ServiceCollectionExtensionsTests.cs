using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kebechet.Blazor.IndexedDB.Tests;

public class ServiceCollectionExtensionsTests
{
    /// <summary>
    /// Scopes are disposed asynchronously, as every Blazor host does: the storage is <c>IAsyncDisposable</c>
    /// only, and a synchronous scope dispose would refuse it.
    /// </summary>
    [Fact]
    public async Task AddIndexedDbStorage_RegistersAScopedStorageOverSingletonOptions()
    {
        // Arrange
        var services = CreateServices();

        // Act
        services.AddIndexedDbStorage();
        await using var serviceProvider = services.BuildServiceProvider();
        await using var firstScope = serviceProvider.CreateAsyncScope();
        await using var secondScope = serviceProvider.CreateAsyncScope();

        // Assert
        var firstStorage = firstScope.ServiceProvider.GetRequiredService<IIndexedDbStorage>();
        var secondStorage = secondScope.ServiceProvider.GetRequiredService<IIndexedDbStorage>();
        firstStorage.ShouldBeOfType<IndexedDbStorage>();
        firstStorage.ShouldNotBeSameAs(secondStorage);
        firstScope.ServiceProvider.GetRequiredService<IndexedDbStorageOptions>()
            .ShouldBeSameAs(secondScope.ServiceProvider.GetRequiredService<IndexedDbStorageOptions>());
    }

    [Fact]
    public void AddIndexedDbStorage_WithoutConfiguration_UsesTheDefaultDatabaseName()
    {
        // Arrange
        var services = CreateServices();

        // Act
        services.AddIndexedDbStorage();
        using var serviceProvider = services.BuildServiceProvider();

        // Assert
        serviceProvider.GetRequiredService<IndexedDbStorageOptions>().DatabaseName.ShouldBe(IndexedDbStorageOptions.DefaultDatabaseName);
    }

    [Fact]
    public void AddIndexedDbStorage_WithConfiguration_AppliesIt()
    {
        // Arrange
        var services = CreateServices();

        // Act
        services.AddIndexedDbStorage(options => options.DatabaseName = "my-app");
        using var serviceProvider = services.BuildServiceProvider();

        // Assert
        serviceProvider.GetRequiredService<IndexedDbStorageOptions>().DatabaseName.ShouldBe("my-app");
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IJSRuntime>());
        services.AddSingleton<ILogger<IndexedDbStorage>>(new RecordingLogger<IndexedDbStorage>());
        return services;
    }
}
