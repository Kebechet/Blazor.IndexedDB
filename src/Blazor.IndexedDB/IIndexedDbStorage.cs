using System.Text.Json;

namespace Kebechet.Blazor.IndexedDB;

/// <summary>
/// Key/value and collection storage over one IndexedDB database. Every value lives in a named store - a logical
/// namespace inside the database - and travels as UTF-8 JSON bytes serialized with the configured
/// <see cref="JsonSerializerOptions"/>, so the interop moves bytes rather than strings and a source-generated
/// context keeps trimming and AOT safe.
/// </summary>
/// <remarks>
/// <para>
/// A store holds key/value entries and, independently of them, one collection written by
/// <see cref="ReplaceCollection{T}"/> and read back by <see cref="ReadCollection{T}"/>. <see cref="Clear"/> wipes both.
/// </para>
/// <para>
/// Nothing here throws on a storage failure. When the browser exposes no working IndexedDB, or the interop fails,
/// reads answer as a miss and writes are dropped and logged; <see cref="GetDriver"/> tells which mode the session
/// runs in.
/// </para>
/// </remarks>
public interface IIndexedDbStorage
{
    /// <summary>Reads the value stored under <paramref name="key"/>, or <c>default</c> when there is none or it cannot be read.</summary>
    Task<T?> Read<T>(string storeName, string key, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="value"/> under <paramref name="key"/>, replacing any previous value.</summary>
    Task Write<T>(string storeName, string key, T value, StorageDurability durability = StorageDurability.Relaxed, CancellationToken cancellationToken = default);

    /// <summary>Removes the entry under <paramref name="key"/>. Removing a missing key is not an error.</summary>
    Task Remove(string storeName, string key, StorageDurability durability = StorageDurability.Relaxed, CancellationToken cancellationToken = default);

    /// <summary>Whether an entry exists under <paramref name="key"/>. Answers <c>false</c> when storage is unavailable.</summary>
    Task<bool> Exists(string storeName, string key, CancellationToken cancellationToken = default);

    /// <summary>Lists the keys of the store's key/value entries. The store's collection is not a key.</summary>
    Task<List<string>> GetKeys(string storeName, CancellationToken cancellationToken = default);

    /// <summary>Reads the store's collection, or an empty list when none was written or it cannot be read.</summary>
    Task<List<T>> ReadCollection<T>(string storeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the store's collection in a single transaction, so a reader never observes a half-written
    /// collection. Key/value entries in the same store are left untouched.
    /// </summary>
    Task ReplaceCollection<T>(string storeName, IReadOnlyCollection<T> values, StorageDurability durability = StorageDurability.Relaxed, CancellationToken cancellationToken = default);

    /// <summary>Removes every key/value entry and the collection of the store.</summary>
    Task Clear(string storeName, StorageDurability durability = StorageDurability.Relaxed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which backend this session's storage runs on. Decided once, on the first operation of the session, and
    /// worth logging: <see cref="StorageDriver.Memory"/> means the data of this session will not survive a restart.
    /// </summary>
    Task<StorageDriver> GetDriver(CancellationToken cancellationToken = default);
}
