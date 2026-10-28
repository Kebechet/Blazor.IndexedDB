[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/kebechet)

# Blazor.IndexedDB
[![NuGet Version](https://img.shields.io/nuget/v/Kebechet.Blazor.IndexedDB)](https://www.nuget.org/packages/Kebechet.Blazor.IndexedDB/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Kebechet.Blazor.IndexedDB)](https://www.nuget.org/packages/Kebechet.Blazor.IndexedDB/)
[![Build](https://github.com/Kebechet/Blazor.IndexedDB/actions/workflows/build.yml/badge.svg)](https://github.com/Kebechet/Blazor.IndexedDB/actions/workflows/build.yml)
[![codecov](https://codecov.io/gh/Kebechet/Blazor.IndexedDB/graph/badge.svg)](https://codecov.io/gh/Kebechet/Blazor.IndexedDB)
[![Storybook](https://img.shields.io/badge/storybook-live%20demo-ff4785)](https://kebechet.github.io/Blazor.IndexedDB/)
![Last updated](https://img.shields.io/github/last-commit/Kebechet/Blazor.IndexedDB/main?label=last%20updated)
[![Twitter](https://img.shields.io/twitter/url/https/twitter.com/samuel_sidor.svg?style=social&label=Follow%20samuel_sidor)](https://x.com/samuel_sidor)

Key/value and collection storage over IndexedDB for Blazor WebAssembly, Blazor Server and .NET MAUI Blazor Hybrid. One database, any number of named stores, values moved as UTF-8 JSON bytes, an atomic collection replace, and an in-memory fallback so a WebView without a working IndexedDB never breaks the app.

**[Live storybook](https://kebechet.github.io/Blazor.IndexedDB/)** - interactive stories for every feature.

## Why

WebView storage is not a given. WKWebView can leave `window.indexedDB` undefined for a whole session after a background launch, and both iOS and Android may evict web storage under pressure. Most IndexedDB wrappers assume the API is there and throw when it is not - which, at startup, means a broken app. This package treats "no storage" as a normal state:

- **Nothing throws.** A failed read is a miss, a failed write is dropped and logged, and `GetDriver()` tells you which mode the session runs in.
- **Bytes, not strings.** Values cross the interop as `byte[]` produced by *your* `JsonSerializerOptions`, so a source-generated context keeps trimming and AOT safe under MAUI on iOS, and a 5k-record cache does not marshal a multi-megabyte string.
- **Atomic collection replace.** `ReplaceCollection` swaps a store's whole collection in one transaction; a reader never sees half old, half new.
- **One database, many stores.** Stores are namespaces inside one IndexedDB database, so listing, replacing or clearing a store is a single key range, not a schema migration.

## Installation

```bash
dotnet add package Kebechet.Blazor.IndexedDB
```

Register the service. Pass your own `JsonSerializerOptions` when you use a source-generated context:

```csharp
using Kebechet.Blazor.IndexedDB;

builder.Services.AddIndexedDbStorage(options =>
{
    options.DatabaseName = "my-app";
    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        TypeInfoResolver = MyJsonContext.Default,
    };
});
```

No script tag: the JS module is imported on demand from the package's static web assets.

## Usage

```razor
@inject IIndexedDbStorage _storage

@code {
    private const string SettingsStore = "settings";
    private const string ExercisesStore = "exercises";

    private async Task Example()
    {
        // key/value
        await _storage.Write(SettingsStore, "theme", new ThemeSettings { IsDark = true });
        var theme = await _storage.Read<ThemeSettings>(SettingsStore, "theme");   // null when missing
        var hasTheme = await _storage.Exists(SettingsStore, "theme");
        var keys = await _storage.GetKeys(SettingsStore);
        await _storage.Remove(SettingsStore, "theme");

        // a store's collection - replaced atomically, read back as a whole
        await _storage.ReplaceCollection(ExercisesStore, exercises);
        var cached = await _storage.ReadCollection<Exercise>(ExercisesStore);    // empty when missing

        // values that must survive a crash right after the write
        await _storage.Write("auth", "refreshToken", token, StorageDurability.Strict);

        // know what this session runs on
        var driver = await _storage.GetDriver();   // IndexedDb, Memory or Unavailable
        await _storage.Clear(ExercisesStore);
    }
}
```

## API

| Member | Behaviour |
|---|---|
| `Read<T>(store, key)` | Value under the key, or `default` when missing or unreadable. |
| `Write<T>(store, key, value, durability)` | Upserts the value. |
| `Remove(store, key, durability)` | Deletes the entry; a missing key is not an error. |
| `Exists(store, key)` | `true` when the entry exists; `false` when storage is unavailable. |
| `GetKeys(store)` | Keys of the store's key/value entries. The collection is not a key. |
| `ReadCollection<T>(store)` | The store's collection, or an empty list. |
| `ReplaceCollection<T>(store, values, durability)` | Replaces the collection in one transaction; key/value entries are untouched. |
| `Clear(store, durability)` | Removes the store's entries and its collection. |
| `GetDriver()` | `IndexedDb`, `Memory` (database could not be opened; data lives until the page unloads) or `Unavailable` (module could not be loaded). |

`StorageDurability` maps to the IndexedDB transaction durability hint: `Relaxed` (default, faster) or `Strict` (flushed before the transaction completes).

### Semantics worth knowing

- A store holds key/value entries **and**, independently, one collection. `Clear` wipes both; `ReplaceCollection` touches only the collection.
- The driver is decided once per session, on the first operation. A database that stops opening mid-session also switches the session to memory - the same rule, applied late.
- An operation whose connection the browser closes under it runs once more on a new connection. WebKit does this when it deletes the site's data while a request is open ("Database deleted by request of the user"); the second attempt sees the database as it is after the deletion, so a read of deleted data is a miss rather than an error. Any other error inside an operation is reported as it is.
- An existing database with the configured name is reused as it is; the package's own object store is added to it with a one-version bump, so it can share a database with other code.
- Every failure is logged through `ILogger<IndexedDbStorage>`: errors per failed operation, one warning when a session falls back to memory. Route those to your error reporting to learn how often real users run degraded.

## Testing

```bash
dotnet test src/Blazor.IndexedDB.slnx                       # the .NET service: serialization, degradation, lifecycle
cd tests/Blazor.IndexedDB.JsTests && npm ci && npm test     # the JS module: IndexedDB path over fake-indexeddb, and the memory fallback
```

## License

[MIT](LICENSE)
