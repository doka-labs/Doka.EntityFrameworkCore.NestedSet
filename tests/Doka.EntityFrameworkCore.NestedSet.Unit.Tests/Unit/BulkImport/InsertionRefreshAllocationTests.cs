namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Budgets unchanged insertion identity paths separately from occupied-heap capacity evidence.</summary>

// WHY: Strict warmed allocation budgets use the executable assembly's established measurement isolation.
[Collection("Allocation measurements")]
public sealed partial class InsertionRefreshAllocationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Captures measured allocation evidence without logging inside the measured operation.</summary>
    /// <param name="output">The current test's evidence output.</param>
    public InsertionRefreshAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Immutable identities reuse snapshots; matching CLR member types also avoid boxed reads.</summary>
    [Theory]
    [InlineData("int", 2048)]
    [InlineData("guid", 2048)]
    [InlineData("compound", 2048)]
    [InlineData("generated", 2048)]
    [InlineData("shadow", 2048)]
    [InlineData("string", 2048)]
    [InlineData("field", 2048)]
    [InlineData("indexer", 482_048)]
    public void Refresh_UnchangedScalarRefresh_ReusesSnapshots(
        string shape,
        long maximumBytes
    )
    {
        // Arrange
        using var context = new AllocationContext();
        var entry = CreateEntry(context, shape);
        var identity = NestedSetInsertionTracking.Capture(entry);
        const int refreshes = 10_000;

        // WHY: JIT and snapshot-cache initialization must finish before the fixed allocation interval starts.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < refreshes; index++)
        {
            identity.Refresh();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: Repeating the same identity measures refresh allocation without adding per-iteration inputs.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < refreshes; index++)
        {
            identity.Refresh();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        // WHY: Property-bag member conversion keeps EF's sentinel-aware object reads (48 bytes for both int reads).
        // The native vector and snapshot arrays remain reused; ordinary scalar and field reads do not box.
        Assert.InRange(allocated, 0, maximumBytes);
        Assert.True(identity.Matches(entry.Metadata.FindPrimaryKey()!.Properties[^1]));
        GC.KeepAlive(identity);
    }

    /// <summary>A changed field-backed key must not match the identity captured before the CLR write.</summary>
    [Fact]
    public void SetClrValueUsingContainingEntity_ChangedFieldIdentity_DoesNotMatchCapturedSnapshot()
    {
        // Arrange
        using var context = new AllocationContext();
        var node = new FieldNode(1);
        var entry = context.Add(node);
        var property = entry.Metadata.FindProperty(nameof(FieldNode.Id))!;
        var identity = NestedSetInsertionTracking.Capture(entry);
        var setter = NestedSetDetachedValueSetter.Get(property)!;

        // Act
        setter.SetClrValueUsingContainingEntity(node, 2);
        var matches = identity.Matches(property);

        // Assert
        Assert.Equal("_id", property.FieldInfo?.Name);
        Assert.Equal(PropertyAccessMode.Field, property.GetPropertyAccessMode());
        Assert.Null(property.PropertyInfo!.SetMethod);
        Assert.Equal(2, node.Id);
        Assert.False(matches);
    }

    /// <summary>Required FK severance remains conceptual null rather than comparing its CLR value as a link.</summary>
    [Fact]
    public void Refresh_RequiredForeignKeyConceptualNull_DoesNotBecomeItsClrValue()
    {
        // Arrange
        using var context = new AllocationContext();
        context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
        context.Add(new ForeignNode { Id = 1 });
        var entry = context.Add(
            new ForeignNode
            {
                Id = 2,
                ParentId = 1,
            });

        var identity = NestedSetInsertionTracking.Capture(entry);
        var parent = entry.Metadata.FindProperty(nameof(ForeignNode.ParentId))!;
        entry.Property(parent)
            .CurrentValue = null;

        var update = NestedSetInsertionTracking.EntryIdentity(entry);

        // Act
        identity.Refresh();

        // Assert
        Assert.True(update.IsConceptualNull(parent));
        Assert.Null(update.GetCurrentValue(parent));
        Assert.Equal(1, entry.Entity.ParentId);
        Assert.True(identity.Matches(entry.Metadata.FindProperty(nameof(ForeignNode.Id))!));
    }

    /// <summary>Changed generated sidecars replace snapshots while the CLR sentinel remains independent.</summary>
    [Fact]
    public void Refresh_GeneratedSidecarRefresh_CapturesTheChangedValue()
    {
        // Arrange
        using var context = new AllocationContext();
        var entry = context.Add(new GeneratedNode());
        var property = entry.Metadata.FindProperty(nameof(GeneratedNode.Id))!;
        var identity = NestedSetInsertionTracking.Capture(entry);
        NestedSetInsertionTracking
            .EntryIdentity(entry)
            .SetStoreGeneratedValue(property, 37, setModified: false);

        // Act
        identity.Refresh();

        // Assert
        Assert.True(identity.Matches(property));
        Assert.Equal(
            37,
            entry.Property(property)
                .CurrentValue);

        Assert.Equal(0, entry.Entity.Id);
    }

    /// <summary>A differing CLR member type preserves EF's configured sentinel conversion.</summary>
    [Fact]
    public void Refresh_IndexerSentinel_UsesTheFrameworkMemberConversion()
    {
        // Arrange
        using var context = new AllocationContext();
        var input = new Dictionary<string, object> { ["Id"] = 0 };
        var entry = context
            .Set<Dictionary<string, object>>("IndexerNode")
            .Add(input);

        var identity = NestedSetInsertionTracking.Capture(entry);
        var property = entry.Metadata.FindProperty("Id")!;
        var current = NestedSetInsertionTracking
            .EntryIdentity(entry)
            .GetCurrentValue<int>(property);

        input["Id"] = 37;

        // WHY: Warm the changed indexer getter and refresh cache outside the measured interval.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < 1000; index++)
        {
            identity.Refresh();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        // WHY: The repeated refresh isolates the framework's member-conversion allocation for one indexer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < 1000; index++)
        {
            identity.Refresh();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.True(identity.Matches(property));
        Assert.Equal(
            current,
            property
                .GetGetter()
                .GetClrValueUsingContainingEntity(input));

        Assert.Equal(37, input["Id"]);
        Assert.InRange(allocated, 0, 50_048);
    }

    /// <summary>Creates a tracked identity with its exact scalar, compound, shadow or generated storage.</summary>
    private static EntityEntry CreateEntry(
        AllocationContext context,
        string shape
    )
    {
        if (shape == "shadow")
        {
            var entry = context.Entry(new ShadowNode());
            entry.Property("Id")
                .CurrentValue = 1;

            entry.State = EntityState.Added;

            return entry;
        }

        if (shape == "indexer")
        {
            return context
                .Set<Dictionary<string, object>>("IndexerNode")
                .Add(new Dictionary<string, object> { ["Id"] = 1 });
        }

        var entity = shape switch
        {
            "int" => (object)new IntNode { Id = 1 },
            "guid" => new GuidNode { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") },
            "compound" => new CompoundNode
            {
                Scope = 1,
                Id = 2,
            },
            "generated" => new GeneratedNode(),
            "string" => new StringNode { Id = "one" },
            "field" => new FieldNode(1),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        return context.Add(entity);
    }

    /// <summary>Requires the null protection supplied by EF's object comparer wrapper.</summary>
    private static bool NonNullTextEquals(
        string? left,
        string? right
    )
    {
        // WHY: Extracting a typed comparer body must not bypass EF's operand null guards.
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return string.Equals(left, right, StringComparison.Ordinal);
    }
}
