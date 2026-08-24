using Shouldly;
using Xunit;

namespace Kebechet.Blazor.IndexedDB.Tests;

public class StorageDurabilityTests
{
    [Theory]
    [InlineData(StorageDurability.Relaxed, "relaxed")]
    [InlineData(StorageDurability.Strict, "strict")]
    public void ToTransactionDurability_ForEveryValue_MapsToTheIndexedDbToken(StorageDurability durability, string expectedToken)
    {
        // Act
        var token = durability.ToTransactionDurability();

        // Assert
        token.ShouldBe(expectedToken);
    }

    [Fact]
    public void ToTransactionDurability_ForAnUnknownValue_Throws()
    {
        // Arrange
        var unknownDurability = (StorageDurability) 42;

        // Act / Assert
        Should.Throw<NotImplementedException>(() => unknownDurability.ToTransactionDurability());
    }
}
