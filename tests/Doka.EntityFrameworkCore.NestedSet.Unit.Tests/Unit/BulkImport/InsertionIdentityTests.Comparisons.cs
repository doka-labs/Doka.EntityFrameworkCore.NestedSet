namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InsertionIdentityTests
{
    /// <summary>Unchanged identity checks and detach preparation retain every scalar key component.</summary>
    /// <param name="shape">The exact assigned, generated, shadow, compound, or member-converted key shape.</param>
    /// <param name="prepare">Whether to measure detachment preparation instead of the callback identity guard.</param>
    [Theory]
    [InlineData("int", false)]
    [InlineData("int", true)]
    [InlineData("guid", false)]
    [InlineData("guid", true)]
    [InlineData("compound", false)]
    [InlineData("compound", true)]
    [InlineData("generated", false)]
    [InlineData("generated", true)]
    [InlineData("shadow", false)]
    [InlineData("shadow", true)]
    [InlineData("string", false)]
    [InlineData("string", true)]
    [InlineData("field", false)]
    [InlineData("field", true)]
    [InlineData("indexer", false)]
    [InlineData("indexer", true)]
    public void CompareIdentity_UnchangedIdentity_RetainsEveryKeyComponent(
        string shape,
        bool prepare
    )
    {
        // Arrange
        using var context = new AllocationContext();
        var entry = CreateEntry(context, shape);
        var identity = NestedSetInsertionTracking.Capture(entry);
        var properties = entry.Metadata.FindPrimaryKey()!.Properties;

        // Act
        var matched = CompareIdentity(identity, properties, prepare);

        // Assert
        Assert.True(matched);
        Assert.All(properties, property => Assert.True(identity.Matches(property)));
    }

    /// <summary>Independent identity capture retains every introduced root and its exact key component.</summary>
    [Fact]
    public void Capture_IndependentRoots_RetainsEveryIdentity()
    {
        // Arrange
        using var context = new AllocationContext();
        const int count = 10_000;
        var entries = new EntityEntry[count];
        var identities = new NestedSetInsertionTracking.Identity[count];

        // WHY: Independent entries prevent an existing identity snapshot from satisfying later captures.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < count; index++)
        {
            entries[index] = context.Add(new IntNode { Id = index + 1 });
        }

        // Act
        // WHY: Aligned entry and result slots retain every independently captured identity for the batch oracle.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < count; index++)
        {
            identities[index] = NestedSetInsertionTracking.Capture(entries[index]);
        }

        // Assert
        var property = entries[0]
            .Metadata
            .FindPrimaryKey()!.Properties[0];

        Assert.All(identities, identity => Assert.True(identity.Matches(property)));
        GC.KeepAlive(identities);
    }

    /// <summary>Executes every requested identity component or its detachment preparation.</summary>
    private static bool CompareIdentity(
        NestedSetInsertionTracking.Identity identity,
        IReadOnlyList<IProperty> properties,
        bool prepare
    )
    {
        if (prepare)
        {
            identity.PrepareDetach();

            return true;
        }

        var matches = true;

        // WHY: Visit every key component without short-circuiting or introducing an iterator in the measured path.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < properties.Count; index++)
        {
            matches &= identity.Matches(properties[index]);
        }

        return matches;
    }
}
