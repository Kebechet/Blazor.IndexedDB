namespace Kebechet.Blazor.IndexedDB;

/// <summary>Which backend a session's storage runs on. Decided once per session, on the first operation.</summary>
public enum StorageDriver
{
    /// <summary>The IndexedDB database. Values persist across launches.</summary>
    IndexedDb,

    /// <summary>
    /// An in-memory fallback, used when the database cannot be opened - for example when a WebView exposes no
    /// <c>indexedDB</c> at all. Values live until the page unloads.
    /// </summary>
    Memory,

    /// <summary>The storage module itself could not be loaded. Every read answers as a miss and every write is dropped.</summary>
    Unavailable,
}
