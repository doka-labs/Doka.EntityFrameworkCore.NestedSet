namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves mapped dictionary membership independently of EF's default-valued indexer getters.</summary>
public sealed partial class PropertyBagRollbackTests
{
    private const string Mapping = "RollbackBag";
    private const string BulkMapping = "RollbackHierarchyBag";
    private const string GeneratedMapping = "RollbackGeneratedBag";
    private const string Flag = "GeneratedFlag";

    /// <summary>
    /// Insertion-owned snapshots distinguish missing generated keys from present null and Boolean values.
    /// </summary>
    /// <param name="shape">The caller's original dictionary membership and value.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("true")]
    public async Task Restore_InsertionValues_RestoreExactGeneratedIndexerMembership(
        string shape
    )
    {
        // Arrange
        await using var context = new BagContext();
        var entity = CreateBag(shape);
        var expected = entity
            .OrderBy(pair => pair.Key)
            .ToArray();

        var snapshot = NestedSetInsertionValues.Capture(context.Model.FindEntityType(Mapping)!, entity)!;
        entity[Flag] = shape != "true";

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(expected, entity.OrderBy(pair => pair.Key));
    }

    /// <summary>Tracked snapshots restore missing generated keys after EF has accepted other changed values.</summary>
    /// <param name="shape">The captured membership and raw value of the generated Boolean indexer.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("true")]
    public async Task Restore_TrackerSnapshot_RestoresExactGeneratedIndexerMembership(
        string shape
    )
    {
        // Arrange
        await using var context = new BagContext();
        var entity = CreateBag(shape);
        context
            .Set<Dictionary<string, object?>>(Mapping)
            .Attach(entity);

        var expected = entity
            .OrderBy(pair => pair.Key)
            .ToArray();

        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        entity[Flag] = shape != "true";
        entity["Payload"] = "changed";
        context.ChangeTracker.DetectChanges();
        context.ChangeTracker.AcceptAllChanges();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(expected, entity.OrderBy(pair => pair.Key));
        Assert.Equal(
            EntityState.Unchanged,
            context
                .Set<Dictionary<string, object?>>(Mapping)
                .Entry(entity)
                .State);

        Assert.False(
            context
                .Set<Dictionary<string, object?>>(Mapping)
                .Entry(entity)
                .Property(Flag)
                .IsModified);
    }

    /// <summary>
    /// Restoring generated keys leaves ordinary and unmapped application payload under caller ownership.
    /// </summary>
    [Fact]
    public async Task Restore_InsertionValues_DoNotCopyOrRestoreApplicationPayload()
    {
        // Arrange
        await using var context = new BagContext();
        var entity = CreateBag("missing");
        var snapshot = NestedSetInsertionValues.Capture(context.Model.FindEntityType(Mapping)!, entity)!;
        entity[Flag] = false;
        entity["Payload"] = "changed";
        entity["ApplicationOnly"] = 42;

        // Act
        snapshot.Restore();

        // Assert
        Assert.False(entity.ContainsKey(Flag));
        Assert.Equal("changed", entity["Payload"]);
        Assert.Equal(42, entity["ApplicationOnly"]);
        Assert.Equal(3, entity.Count);
    }

    /// <summary>
    /// Bulk sparse geometry retains missing keys and present nulls without boxing ordinary coordinates.
    /// </summary>
    /// <param name="shape">The original detached dictionary representation.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("sentinel")]
    public async Task Capture_BulkStructuralSnapshot_DistinguishesMissingNullAndSentinel(
        string shape
    )
    {
        // Arrange
        await using var context = new BagContext();
        var type = context.Model.FindEntityType(BulkMapping)!;
        var map = NestedSetMapping<Dictionary<string, object?>, int, NestedSetNoScope>.For(context, type);
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = 1,
            ["Payload"] = "before",
        };

        if (shape != "missing")
        {
            entity[map.TreeId] = shape == "null"
                ? null
                : Guid.Empty;

            entity[map.Parent] = null;
            entity[map.Left] = shape == "null"
                ? null
                : 0L;

            entity[map.Right] = shape == "null"
                ? null
                : 0L;

            entity[map.Depth] = shape == "null"
                ? null
                : 0;

            entity[map.Position] = shape == "null"
                ? null
                : 0L;
        }

        // Act
        var original = NestedSetBulkPlan<Dictionary<string, object?>, int, Guid, NestedSetNoScope>.OriginalStructure
            .Capture(entity, map);

        // Assert
        if (shape == "sentinel")
        {
            Assert.Null(original);
        }
        else
        {
            Assert.NotNull(original);
            Assert.Equal(shape == "missing", original.IsMissing(1 << 1));
            Assert.Equal(shape == "missing", original.IsMissing(1 << 3));
            Assert.Equal(shape == "missing", original.IsMissing(1 << 4));
            Assert.Equal(shape == "missing", original.IsMissing(1 << 5));
            Assert.Equal(shape == "missing", original.IsMissing(1 << 6));

            if (shape == "null")
            {
                Assert.Null(original.TreeIdOr(Guid.Empty));
                Assert.Null(original.ParentOr(null));
                Assert.Null(original.LeftOr(0L));
                Assert.Null(original.RightOr(0L));
                Assert.Null(original.DepthOr(0));
                Assert.Null(original.PositionOr(0L));
            }
        }
    }

    /// <summary>Custom indexers without a dictionary contract retain their existing EF accessor restoration.</summary>
    /// <param name="original">The generated indexer value present before the rejected insertion.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_NonDictionaryIndexer_UsesItsMappedSetter(
        bool original
    )
    {
        // Arrange
        await using var context = new BagContext();
        var entity = new CustomIndexerEntity
        {
            Id = 1,
            [Flag] = original,
        };

        var snapshot = NestedSetInsertionValues.Capture(
            context.Model.FindEntityType(typeof(CustomIndexerEntity))!,
            entity)!;

        entity[Flag] = !original;

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(original, entity[Flag]);
        Assert.Equal(1, entity.Id);
    }

    /// <summary>
    /// Generated integer indexers classify raw absence/null separately from an explicitly supplied zero.
    /// </summary>
    /// <param name="shape">The original caller-owned indexer representation.</param>
    /// <param name="expected">Whether EF considers that raw value explicitly assigned.</param>
    [Theory]
    [InlineData("missing", false)]
    [InlineData("null", false)]
    [InlineData("zero", true)]
    [InlineData("assigned", true)]
    public async Task HasAssignedKey_GeneratedKeyAssignment_UsesRawIndexerSentinel(
        string shape,
        bool expected
    )
    {
        // Arrange
        await using var context = new BagContext();
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (shape != "missing")
        {
            entity["Id"] = shape == "null"
                ? null
                : shape == "zero"
                    ? 0
                    : 17;
        }

        var entry = context
            .Set<Dictionary<string, object?>>(GeneratedMapping)
            .Entry(entity);

        var property = entry.Metadata.FindProperty("Id")!;
        var normalized = entry.Property<int>("Id")
            .CurrentValue;

        // Act
        var assigned = NestedSetInsertionValues.HasAssignedKey(property, entity, normalized);

        // Assert
        Assert.Null(property.Sentinel);
        Assert.Equal(expected, assigned);
        Assert.Equal(shape != "missing", entity.ContainsKey("Id"));
    }

    /// <summary>
    /// Builds original consumer state without preinitializing any implementation-owned generated flag.
    /// </summary>
    private static Dictionary<string, object?> CreateBag(
        string shape
    )
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = 1,
            ["Payload"] = "before",
        };

        if (shape != "missing")
        {
            entity[Flag] = shape == "null"
                ? null
                : shape == "true";
        }

        return entity;
    }
}
