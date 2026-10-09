namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InsertionRefreshAllocationTests
{
    /// <summary>Unchanged identity checks and preparation retain scalar values without fresh boxes.</summary>
    /// <param name="shape">The exact assigned, generated, shadow, compound, or member-converted key shape.</param>
    /// <param name="prepare">Whether to measure detachment preparation instead of the callback identity guard.</param>
    /// <param name="maximumBytes">The allocation ceiling across ten thousand warmed operations.</param>
    [Theory]
    [InlineData("int", false, 2048)]
    [InlineData("int", true, 2048)]
    [InlineData("guid", false, 2048)]
    [InlineData("guid", true, 2048)]
    [InlineData("compound", false, 2048)]
    [InlineData("compound", true, 2048)]
    [InlineData("generated", false, 2048)]
    [InlineData("generated", true, 2048)]
    [InlineData("shadow", false, 2048)]
    [InlineData("shadow", true, 2048)]
    [InlineData("string", false, 2048)]
    [InlineData("string", true, 2048)]
    [InlineData("field", false, 2048)]
    [InlineData("field", true, 2048)]
    [InlineData("indexer", false, 482_048)]
    [InlineData("indexer", true, 722_048)]
    public void UnchangedIdentityComparisonsAvoidBoxes(
        string shape,
        bool prepare,
        long maximumBytes
    )
    {
        // Arrange
        using var context = new AllocationContext();
        var entry = CreateEntry(context, shape);
        var identity = NestedSetInsertionTracking.Capture(entry);
        var properties = entry.Metadata.FindPrimaryKey()!.Properties;
        const int repetitions = 10_000;

        for (var index = 0; index < repetitions; index++)
        {
            _ = CompareIdentity(identity, properties, prepare);
        }

        var matched = true;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < repetitions; index++)
        {
            matched &= CompareIdentity(identity, properties, prepare);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        _output.WriteLine($"Shape={shape}; prepare={prepare}; operations={repetitions}; allocated={allocated} B.");
        Assert.True(matched);
        Assert.InRange(allocated, 0, maximumBytes);
        Assert.All(properties, property => Assert.True(identity.Matches(property)));
        GC.KeepAlive(identity);
    }

    /// <summary>Identity capture does not allocate empty dependent-bucket storage for each independent root.</summary>
    [Fact]
    public void IndependentRootCaptureHasABoundedInitialAllocation()
    {
        // Arrange
        using var context = new AllocationContext();
        const int count = 10_000;
        var entries = new EntityEntry[count];
        var identities = new NestedSetInsertionTracking.Identity[count];

        for (var index = 0; index < count; index++)
        {
            entries[index] = context.Add(new IntNode { Id = index + 1 });
        }

        for (var index = 0; index < 1000; index++)
        {
            _ = NestedSetInsertionTracking.Capture(entries[0]);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < count; index++)
        {
            identities[index] = NestedSetInsertionTracking.Capture(entries[index]);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        _output.WriteLine($"Independent roots={count}; initial capture={allocated} B; bytes/root={allocated / count}.");

        // WHY: The measured scalar snapshot costs 272 bytes; 280 leaves no room for an empty list per root.
        Assert.InRange(allocated, 0, count * 280L + 2048);
        var property = entries[0]
            .Metadata
            .FindPrimaryKey()!.Properties[0];
        Assert.All(identities, identity => Assert.True(identity.Matches(property)));
        GC.KeepAlive(identities);
    }

    /// <summary>Executes the warmed path without creating property wrappers or assertion delegates.</summary>
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

        for (var index = 0; index < properties.Count; index++)
        {
            matches &= identity.Matches(properties[index]);
        }

        return matches;
    }
}
