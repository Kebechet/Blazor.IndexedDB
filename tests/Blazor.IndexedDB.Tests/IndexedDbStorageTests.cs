using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Kebechet.Blazor.IndexedDB.Tests;

public class IndexedDbStorageTests
{
    private const string _store = "settings";
    private const string _key = "theme";

    /// <summary>Verbatim WKWebView failure that motivated the in-memory fallback.</summary>
    private const string _indexedDbUnavailableMessage = "Can't find variable: indexedDB";

    private readonly IJSRuntime _jsRuntime = Substitute.For<IJSRuntime>();
    private readonly IJSObjectReference _module = Substitute.For<IJSObjectReference>();
    private readonly IJSObjectReference _jsStorage = Substitute.For<IJSObjectReference>();
    private readonly RecordingLogger<IndexedDbStorage> _logger = new();

    /// <summary>
    /// A naming policy the defaults do not use, so a value only round-trips when the service honours the
    /// configured options on both the read and the write side.
    /// </summary>
    private readonly IndexedDbStorageOptions _options = new()
    {
        DatabaseName = "test-db",
        JsonSerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower },
    };

    public IndexedDbStorageTests()
    {
        _jsRuntime
            .InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(_module);
        _module
            .InvokeAsync<IJSObjectReference>("createStorage", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(_jsStorage);
        _jsStorage
            .InvokeAsync<string>("probe", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns("indexedDb");
    }

    [Fact]
    public async Task Read_WhenValueIsStored_DeserializesWithConfiguredOptions()
    {
        // Arrange
        var storage = CreateStorage();
        ReturnBytesFrom("get", "{\"first_name\":\"Ada\",\"count\":3}");

        // Act
        var sample = await storage.Read<Sample>(_store, _key);

        // Assert
        sample.ShouldNotBeNull();
        sample.FirstName.ShouldBe("Ada");
        sample.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Read_WhenKeyIsMissing_ReturnsDefault()
    {
        // Arrange
        var storage = CreateStorage();
        _jsStorage
            .InvokeAsync<byte[]?>("get", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns((byte[]?) null);

        // Act
        var sample = await storage.Read<Sample>(_store, _key);

        // Assert
        sample.ShouldBeNull();
    }

    [Fact]
    public async Task Read_WhenPayloadIsCorrupt_ReturnsDefaultAndLogsError()
    {
        // Arrange
        var storage = CreateStorage();
        ReturnBytesFrom("get", "this is not json");

        // Act
        var sample = await storage.Read<Sample>(_store, _key);

        // Assert
        sample.ShouldBeNull();
        _logger.Entries.ShouldContain(x => x.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Read_WhenInteropThrows_ReturnsDefault()
    {
        // Arrange
        var storage = CreateStorage();
        MakeEveryStorageCallThrow();

        // Act
        var sample = await storage.Read<Sample>(_store, _key);

        // Assert
        sample.ShouldBeNull();
    }

    [Fact]
    public async Task Write_SerializesWithConfiguredOptionsAndPassesDurability()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        await storage.Write(_store, _key, new Sample("Ada", 3), StorageDurability.Strict);

        // Assert
        await _jsStorage
            .Received(1)
            .InvokeAsync<IJSVoidResult>("set", Arg.Any<CancellationToken>(), Arg.Is<object?[]>(x =>
                (string) x[0]! == _store &&
                (string) x[1]! == _key &&
                Encoding.UTF8.GetString((byte[]) x[2]!) == "{\"first_name\":\"Ada\",\"count\":3}" &&
                (string) x[3]! == "strict"));
    }

    [Fact]
    public async Task Write_WithoutDurability_DefaultsToRelaxed()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        await storage.Write(_store, _key, "dark");

        // Assert
        await _jsStorage
            .Received(1)
            .InvokeAsync<IJSVoidResult>("set", Arg.Any<CancellationToken>(), Arg.Is<object?[]>(x => (string) x[3]! == "relaxed"));
    }

    [Fact]
    public async Task Write_WhenInteropThrows_DoesNotThrowAndLogsError()
    {
        // Arrange
        var storage = CreateStorage();
        MakeEveryStorageCallThrow();

        // Act / Assert
        await Should.NotThrowAsync(() => storage.Write(_store, _key, "dark"));
        _logger.Entries.ShouldContain(x => x.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Remove_PassesStoreKeyAndDurability()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        await storage.Remove(_store, _key, StorageDurability.Strict);

        // Assert
        await _jsStorage
            .Received(1)
            .InvokeAsync<IJSVoidResult>("remove", Arg.Any<CancellationToken>(), Arg.Is<object?[]>(x =>
                (string) x[0]! == _store &&
                (string) x[1]! == _key &&
                (string) x[2]! == "strict"));
    }

    [Fact]
    public async Task Remove_WhenInteropThrows_DoesNotThrow()
    {
        // Arrange
        var storage = CreateStorage();
        MakeEveryStorageCallThrow();

        // Act / Assert
        await Should.NotThrowAsync(() => storage.Remove(_store, _key));
    }

    [Fact]
    public async Task Exists_WhenKeyIsStored_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();
        _jsStorage
            .InvokeAsync<bool>("containsKey", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(true);

        // Act
        var doesExist = await storage.Exists(_store, _key);

        // Assert
        doesExist.ShouldBeTrue();
    }

    [Fact]
    public async Task Exists_WhenInteropThrows_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        MakeEveryStorageCallThrow();

        // Act
        var doesExist = await storage.Exists(_store, _key);

        // Assert
        doesExist.ShouldBeFalse();
    }

    [Fact]
    public async Task GetKeys_ReturnsTheStoreKeys()
    {
        // Arrange
        var storage = CreateStorage();
        _jsStorage
            .InvokeAsync<List<string>>("keys", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(new List<string> { "theme", "locale" });

        // Act
        var keys = await storage.GetKeys(_store);

        // Assert
        keys.ShouldBe(new[] { "theme", "locale" });
    }

    [Fact]
    public async Task GetKeys_WhenInteropThrows_ReturnsEmptyList()
    {
        // Arrange
        var storage = CreateStorage();
        MakeEveryStorageCallThrow();

        // Act
        var keys = await storage.GetKeys(_store);

        // Assert
        keys.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadCollection_WhenStored_DeserializesTheList()
    {
        // Arrange
        var storage = CreateStorage();
        ReturnBytesFrom("getCollection", "[{\"first_name\":\"Ada\",\"count\":1},{\"first_name\":\"Grace\",\"count\":2}]");

        // Act
        var samples = await storage.ReadCollection<Sample>(_store);

        // Assert
        samples.Count.ShouldBe(2);
        samples[1].FirstName.ShouldBe("Grace");
    }

    [Fact]
    public async Task ReadCollection_WhenMissing_ReturnsEmptyList()
    {
        // Arrange
        var storage = CreateStorage();
        _jsStorage
            .InvokeAsync<byte[]?>("getCollection", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns((byte[]?) null);

        // Act
        var samples = await storage.ReadCollection<Sample>(_store);

        // Assert
        samples.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadCollection_WhenPayloadIsCorrupt_ReturnsEmptyList()
    {
        // Arrange
        var storage = CreateStorage();
        ReturnBytesFrom("getCollection", "[not json");

        // Act
        var samples = await storage.ReadCollection<Sample>(_store);

        // Assert
        samples.ShouldBeEmpty();
        _logger.Entries.ShouldContain(x => x.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ReadCollection_WhenInteropThrows_ReturnsEmptyList()
    {
        // Arrange
        var storage = CreateStorage();
        MakeEveryStorageCallThrow();

        // Act
        var samples = await storage.ReadCollection<Sample>(_store);

        // Assert
        samples.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReplaceCollection_SerializesTheListAndPassesDurability()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        await storage.ReplaceCollection(_store, new[] { new Sample("Ada", 1) }, StorageDurability.Strict);

        // Assert
        await _jsStorage
            .Received(1)
            .InvokeAsync<IJSVoidResult>("replaceCollection", Arg.Any<CancellationToken>(), Arg.Is<object?[]>(x =>
                (string) x[0]! == _store &&
                Encoding.UTF8.GetString((byte[]) x[1]!) == "[{\"first_name\":\"Ada\",\"count\":1}]" &&
                (string) x[2]! == "strict"));
    }

    [Fact]
    public async Task ReplaceCollection_WhenInteropThrows_DoesNotThrow()
    {
        // Arrange
        var storage = CreateStorage();
        MakeEveryStorageCallThrow();

        // Act / Assert
        await Should.NotThrowAsync(() => storage.ReplaceCollection(_store, new List<Sample>()));
    }

    [Fact]
    public async Task Clear_PassesStoreAndDurability()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        await storage.Clear(_store, StorageDurability.Strict);

        // Assert
        await _jsStorage
            .Received(1)
            .InvokeAsync<IJSVoidResult>("clear", Arg.Any<CancellationToken>(), Arg.Is<object?[]>(x =>
                (string) x[0]! == _store &&
                (string) x[1]! == "strict"));
    }

    [Fact]
    public async Task Clear_WhenInteropThrows_DoesNotThrow()
    {
        // Arrange
        var storage = CreateStorage();
        MakeEveryStorageCallThrow();

        // Act / Assert
        await Should.NotThrowAsync(() => storage.Clear(_store));
    }

    [Fact]
    public async Task GetDriver_WhenProbeReportsIndexedDb_ReturnsIndexedDbWithoutWarning()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var driver = await storage.GetDriver();

        // Assert
        driver.ShouldBe(StorageDriver.IndexedDb);
        _logger.Entries.ShouldNotContain(x => x.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GetDriver_WhenProbeReportsMemory_ReturnsMemoryAndWarnsOnce()
    {
        // Arrange
        var storage = CreateStorage();
        _jsStorage
            .InvokeAsync<string>("probe", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns("memory");

        // Act
        var driver = await storage.GetDriver();
        await storage.Read<string>(_store, _key);
        await storage.GetDriver();

        // Assert
        driver.ShouldBe(StorageDriver.Memory);
        _logger.Entries.Count(x => x.Level == LogLevel.Warning).ShouldBe(1);
    }

    [Fact]
    public async Task GetDriver_WhenModuleImportFails_ReturnsUnavailableAndOperationsReadAsMiss()
    {
        // Arrange
        var storage = CreateStorage();
        _jsRuntime
            .InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Throws(new JSException("Failed to fetch dynamically imported module"));

        // Act
        var driver = await storage.GetDriver();
        var value = await storage.Read<string>(_store, _key);
        var doesExist = await storage.Exists(_store, _key);

        // Assert
        driver.ShouldBe(StorageDriver.Unavailable);
        value.ShouldBeNull();
        doesExist.ShouldBeFalse();
        _logger.Entries.ShouldContain(x => x.Level == LogLevel.Error);
        await _jsStorage.DidNotReceiveWithAnyArgs().InvokeAsync<byte[]?>(default!, default(CancellationToken), default);
    }

    [Fact]
    public async Task Initialization_AcrossSeveralOperations_ImportsAndProbesOnce()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        await storage.Exists(_store, "first");
        await storage.Read<string>(_store, "second");
        await storage.Write(_store, "third", "value");

        // Assert
        await _jsRuntime.Received(1).InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>());
        await _module.Received(1).InvokeAsync<IJSObjectReference>("createStorage", Arg.Any<CancellationToken>(), Arg.Any<object?[]>());
        await _jsStorage.Received(1).InvokeAsync<string>("probe", Arg.Any<CancellationToken>(), Arg.Any<object?[]>());
    }

    [Fact]
    public async Task Initialization_BindsTheJsStorageToTheConfiguredDatabase()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        await storage.GetDriver();

        // Assert
        await _module
            .Received(1)
            .InvokeAsync<IJSObjectReference>("createStorage", Arg.Any<CancellationToken>(), Arg.Is<object?[]>(x => (string) x[0]! == "test-db"));
    }

    [Fact]
    public async Task Initialization_WhenTheFirstAttemptIsCancelled_RetriesOnTheNextCall()
    {
        // Arrange
        var storage = CreateStorage();
        _jsRuntime
            .InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(
                _ => throw new OperationCanceledException(),
                _ => new ValueTask<IJSObjectReference>(_module));

        // Act
        await Should.ThrowAsync<OperationCanceledException>(() => storage.GetDriver());
        var driver = await storage.GetDriver();

        // Assert
        driver.ShouldBe(StorageDriver.IndexedDb);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheJsStorageAndTheModule()
    {
        // Arrange
        var storage = CreateStorage();
        await storage.GetDriver();

        // Act
        await storage.DisposeAsync();

        // Assert
        await _jsStorage.Received(1).DisposeAsync();
        await _module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenTheCircuitIsGone_DoesNotThrow()
    {
        // Arrange
        var storage = CreateStorage();
        await storage.GetDriver();
        _jsStorage.DisposeAsync().Returns(ValueTask.FromException(new JSDisconnectedException("circuit gone")));

        // Act / Assert
        await Should.NotThrowAsync(async () => await storage.DisposeAsync());
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DisposesOnce()
    {
        // Arrange
        var storage = CreateStorage();
        await storage.GetDriver();

        // Act
        await storage.DisposeAsync();
        await storage.DisposeAsync();

        // Assert
        await _module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Read_AfterDispose_ReadsAsMissWithoutInterop()
    {
        // Arrange
        var storage = CreateStorage();
        await storage.DisposeAsync();

        // Act
        var value = await storage.Read<string>(_store, _key);

        // Assert
        value.ShouldBeNull();
        await _jsRuntime.DidNotReceiveWithAnyArgs().InvokeAsync<IJSObjectReference>(default!, default(CancellationToken), default);
    }

    private IndexedDbStorage CreateStorage()
    {
        return new IndexedDbStorage(_jsRuntime, _options, _logger);
    }

    private void ReturnBytesFrom(string function, string json)
    {
        _jsStorage
            .InvokeAsync<byte[]?>(function, Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(Encoding.UTF8.GetBytes(json));
    }

    private void MakeEveryStorageCallThrow()
    {
        var exception = new JSException(_indexedDbUnavailableMessage);
        _jsStorage
            .InvokeAsync<byte[]?>(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Throws(exception);
        _jsStorage
            .InvokeAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Throws(exception);
        _jsStorage
            .InvokeAsync<List<string>>(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Throws(exception);
        _jsStorage
            .InvokeAsync<IJSVoidResult>(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Throws(exception);
    }

    private sealed record Sample(string FirstName, int Count);
}
