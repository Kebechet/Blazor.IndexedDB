using System.Text.Json;

namespace Kebechet.Blazor.IndexedDB;

/// <summary>Configuration of <see cref="IIndexedDbStorage"/>, applied through <c>AddIndexedDbStorage</c>.</summary>
public sealed class IndexedDbStorageOptions
{
    /// <summary>Database name used when <see cref="DatabaseName"/> is not set.</summary>
    public const string DefaultDatabaseName = "kebechet-indexeddb";

    /// <summary>
    /// Name of the IndexedDB database. One database per application is enough - stores are namespaces inside it.
    /// An existing database is reused as it is; the storage's own object store is added to it on first use.
    /// </summary>
    public string DatabaseName { get; set; } = DefaultDatabaseName;

    /// <summary>
    /// Options every value is serialized and deserialized with. Pass a source-generated
    /// <c>JsonSerializerContext</c> through <c>TypeInfoResolver</c> to stay trim and AOT safe, which matters
    /// under MAUI on iOS.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions(JsonSerializerDefaults.Web);
}
