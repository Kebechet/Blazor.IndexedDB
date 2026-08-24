namespace Kebechet.Blazor.IndexedDB;

/// <summary>The IndexedDB transaction durability hint a write is made with.</summary>
public enum StorageDurability
{
    /// <summary>Let the browser flush to disk when it sees fit. The default, and the faster choice for cache-like data.</summary>
    Relaxed,

    /// <summary>
    /// Ask the browser to flush to disk before the transaction completes. For values that must survive a crash
    /// right after the write, such as authentication tokens.
    /// </summary>
    Strict,
}

internal static class StorageDurabilityExtensions
{
    /// <summary>
    /// Maps to the wire token of the IndexedDB <c>durability</c> transaction option, as handed to the JS side.
    /// Pinned as literals: they are part of the JS contract, not code symbols.
    /// </summary>
    public static string ToTransactionDurability(this StorageDurability durability)
    {
        return durability switch
        {
            StorageDurability.Relaxed => "relaxed",
            StorageDurability.Strict => "strict",
            _ => throw new NotImplementedException($"{nameof(ToTransactionDurability)} has no mapping for {durability}."),
        };
    }
}
