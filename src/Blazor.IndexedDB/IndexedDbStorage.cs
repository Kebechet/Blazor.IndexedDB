using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Kebechet.Blazor.IndexedDB;

/// <inheritdoc cref="IIndexedDbStorage"/>
public sealed class IndexedDbStorage : IIndexedDbStorage, IAsyncDisposable
{
    private const string _modulePath = "./_content/Kebechet.Blazor.IndexedDB/indexeddb-storage.js";
    private const string _createStorageFunction = "createStorage";
    private const string _probeFunction = "probe";
    private const string _getFunction = "get";
    private const string _setFunction = "set";
    private const string _removeFunction = "remove";
    private const string _containsKeyFunction = "containsKey";
    private const string _keysFunction = "keys";
    private const string _getCollectionFunction = "getCollection";
    private const string _replaceCollectionFunction = "replaceCollection";
    private const string _clearFunction = "clear";

    /// <summary>Driver name the JS side reports once it runs on its in-memory store. Part of the JS contract.</summary>
    private const string _memoryDriverName = "memory";

    private readonly IJSRuntime _jsRuntime;
    private readonly IndexedDbStorageOptions _options;
    private readonly ILogger<IndexedDbStorage> _logger;
    private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);

    private IJSObjectReference? _module;
    private IJSObjectReference? _storage;
    private StorageDriver? _driver;
    private bool _isDisposed;

    /// <summary>Creates the service. Prefer <c>AddIndexedDbStorage()</c> over constructing it directly.</summary>
    public IndexedDbStorage(IJSRuntime jsRuntime, IndexedDbStorageOptions options, ILogger<IndexedDbStorage> logger)
    {
        _jsRuntime = jsRuntime;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<T?> Read<T>(string storeName, string key, CancellationToken cancellationToken = default)
    {
        var storage = await TryGetStorage(cancellationToken);
        if (storage is null)
        {
            return default;
        }

        try
        {
            var utf8Json = await storage.InvokeAsync<byte[]?>(_getFunction, cancellationToken, storeName, key);
            if (utf8Json is null || utf8Json.Length == 0)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(utf8Json, _options.JsonSerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException || ex is JSException)
        {
            _logger.LogError(ex, "Failed to read key '{Key}' from store '{Store}'.", key, storeName);
            return default;
        }
    }

    /// <inheritdoc />
    public async Task Write<T>(string storeName, string key, T value, StorageDurability durability = StorageDurability.Relaxed, CancellationToken cancellationToken = default)
    {
        var storage = await TryGetStorage(cancellationToken);
        if (storage is null)
        {
            return;
        }

        var utf8Json = JsonSerializer.SerializeToUtf8Bytes(value, _options.JsonSerializerOptions);
        try
        {
            await storage.InvokeVoidAsync(_setFunction, cancellationToken, storeName, key, utf8Json, durability.ToTransactionDurability());
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "Failed to write key '{Key}' to store '{Store}'.", key, storeName);
        }
    }

    /// <inheritdoc />
    public async Task Remove(string storeName, string key, StorageDurability durability = StorageDurability.Relaxed, CancellationToken cancellationToken = default)
    {
        var storage = await TryGetStorage(cancellationToken);
        if (storage is null)
        {
            return;
        }

        try
        {
            await storage.InvokeVoidAsync(_removeFunction, cancellationToken, storeName, key, durability.ToTransactionDurability());
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "Failed to remove key '{Key}' from store '{Store}'.", key, storeName);
        }
    }

    /// <inheritdoc />
    public async Task<bool> Exists(string storeName, string key, CancellationToken cancellationToken = default)
    {
        var storage = await TryGetStorage(cancellationToken);
        if (storage is null)
        {
            return false;
        }

        try
        {
            return await storage.InvokeAsync<bool>(_containsKeyFunction, cancellationToken, storeName, key);
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "Failed to check key '{Key}' in store '{Store}'.", key, storeName);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<List<string>> GetKeys(string storeName, CancellationToken cancellationToken = default)
    {
        var storage = await TryGetStorage(cancellationToken);
        if (storage is null)
        {
            return new List<string>();
        }

        try
        {
            return await storage.InvokeAsync<List<string>>(_keysFunction, cancellationToken, storeName);
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "Failed to list the keys of store '{Store}'.", storeName);
            return new List<string>();
        }
    }

    /// <inheritdoc />
    public async Task<List<T>> ReadCollection<T>(string storeName, CancellationToken cancellationToken = default)
    {
        var storage = await TryGetStorage(cancellationToken);
        if (storage is null)
        {
            return new List<T>();
        }

        try
        {
            var utf8Json = await storage.InvokeAsync<byte[]?>(_getCollectionFunction, cancellationToken, storeName);
            if (utf8Json is null || utf8Json.Length == 0)
            {
                return new List<T>();
            }

            return JsonSerializer.Deserialize<List<T>>(utf8Json, _options.JsonSerializerOptions) ?? new List<T>();
        }
        catch (Exception ex) when (ex is JsonException || ex is JSException)
        {
            _logger.LogError(ex, "Failed to read the collection of store '{Store}'.", storeName);
            return new List<T>();
        }
    }

    /// <inheritdoc />
    public async Task ReplaceCollection<T>(string storeName, IReadOnlyCollection<T> values, StorageDurability durability = StorageDurability.Relaxed, CancellationToken cancellationToken = default)
    {
        var storage = await TryGetStorage(cancellationToken);
        if (storage is null)
        {
            return;
        }

        var valuesList = values as List<T> ?? new List<T>(values);
        var utf8Json = JsonSerializer.SerializeToUtf8Bytes(valuesList, _options.JsonSerializerOptions);
        try
        {
            await storage.InvokeVoidAsync(_replaceCollectionFunction, cancellationToken, storeName, utf8Json, durability.ToTransactionDurability());
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "Failed to replace the collection of store '{Store}'.", storeName);
        }
    }

    /// <inheritdoc />
    public async Task Clear(string storeName, StorageDurability durability = StorageDurability.Relaxed, CancellationToken cancellationToken = default)
    {
        var storage = await TryGetStorage(cancellationToken);
        if (storage is null)
        {
            return;
        }

        try
        {
            await storage.InvokeVoidAsync(_clearFunction, cancellationToken, storeName, durability.ToTransactionDurability());
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "Failed to clear store '{Store}'.", storeName);
        }
    }

    /// <inheritdoc />
    public async Task<StorageDriver> GetDriver(CancellationToken cancellationToken = default)
    {
        await TryGetStorage(cancellationToken);
        return _driver ?? StorageDriver.Unavailable;
    }

    /// <summary>
    /// Imports the module, creates the JS storage bound to the configured database and lets it pick its driver -
    /// once per session. A module that cannot be loaded leaves the session <see cref="StorageDriver.Unavailable"/>
    /// rather than retrying on every call; a cancelled first attempt is retried by the next call.
    /// </summary>
    private async Task<IJSObjectReference?> TryGetStorage(CancellationToken cancellationToken)
    {
        if (_driver is not null || _isDisposed)
        {
            return _storage;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_driver is not null || _isDisposed)
            {
                return _storage;
            }

            try
            {
                _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, _modulePath);
                _storage = await _module.InvokeAsync<IJSObjectReference>(_createStorageFunction, cancellationToken, _options.DatabaseName);

                var driverName = await _storage.InvokeAsync<string>(_probeFunction, cancellationToken);
                _driver = driverName == _memoryDriverName
                    ? StorageDriver.Memory
                    : StorageDriver.IndexedDb;

                if (_driver == StorageDriver.Memory)
                {
                    _logger.LogWarning("IndexedDB is unavailable in this session; storage '{Database}' runs in memory until the page reloads.", _options.DatabaseName);
                }
            }
            catch (JSException ex)
            {
                _logger.LogError(ex, "The IndexedDB storage module could not be loaded; every operation on '{Database}' reads as a miss.", _options.DatabaseName);
                _driver = StorageDriver.Unavailable;
                _storage = null;
            }

            return _storage;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            if (_storage is not null)
            {
                await _storage.DisposeAsync();
                _storage = null;
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
                _module = null;
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit already gone - nothing to clean up on the JS side.
        }

        _initializationLock.Dispose();
    }
}
