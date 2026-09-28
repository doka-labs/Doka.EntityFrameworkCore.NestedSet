namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies public live typed reads for nullable parents, sidecars and bounded allocation.</summary>
// WHY: The fixed allocation budget must not include runtime work triggered by concurrent metadata probes.
[Collection("Allocation measurements")]
public sealed partial class TypedPropertyReadTests
{
    /// <summary>A present zero-valued parent remains distinct from an absent integer parent.</summary>
    /// <param name="present">Whether the live parent contains the default key value.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntegerParentReadsLiveNullablePresence(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var node = new ReadNode
        {
            Id = 1,
            IntegerParent = 7,
        };

        var entry = context.Attach(node);
        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(nameof(ReadNode.IntegerParent))!;

        // Act
        node.IntegerParent = present ? 0 : null;
        var parent = NestedSetParent<int>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(0, parent.Value);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>A present empty Guid remains distinct from an absent Guid parent.</summary>
    /// <param name="present">Whether the live parent contains Guid.Empty.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuidParentReadsLiveNullablePresence(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var node = new ReadNode
        {
            Id = 1,
            GuidParent = Guid.NewGuid(),
        };

        var entry = context.Attach(node);
        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(nameof(ReadNode.GuidParent))!;

        // Act
        node.GuidParent = present ? Guid.Empty : null;
        var parent = NestedSetParent<Guid>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(Guid.Empty, parent.Value);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Nullable reference storage remains live after the wrapper was created.</summary>
    /// <param name="present">Whether the live parent contains an empty string rather than null.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceParentReadsLivePresence(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var node = new ReadNode
        {
            Id = 1,
            ReferenceParent = "initial",
        };

        var entry = context.Attach(node);
        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(nameof(ReadNode.ReferenceParent))!;

        // Act
        node.ReferenceParent = present ? string.Empty : null;
        var parent = NestedSetParent<string>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(node.ReferenceParent, parent.Value);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Domain inequality operators cannot hide a non-null converted reference parent.</summary>
    /// <param name="present">Whether the live converted reference is present.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertedReferencePresenceIgnoresDomainOperators(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var node = new ReadNode
        {
            Id = 1,
            ConvertedReferenceParent = new ReferenceKey("initial"),
        };

        var entry = context.Attach(node);
        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(nameof(ReadNode.ConvertedReferenceParent))!;

        // Act
        node.ConvertedReferenceParent = present ? new ReferenceKey("live") : null;
        var parent = NestedSetParent<ReferenceKey>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Same(node.ConvertedReferenceParent, parent.Value);
        Assert.NotNull(property.GetTypeMapping().Converter);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>A nullable converted struct does not require equality operators for live parent reads.</summary>
    /// <param name="present">Whether the live optional struct contains its default key value.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatorFreeStructReadsLiveNullablePresence(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var node = new ReadNode
        {
            Id = 1,
            StructParent = new ValueKey(7),
        };

        var entry = context.Attach(node);
        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(nameof(ReadNode.StructParent))!;

        // Act
        node.StructParent = present ? new ValueKey(0) : null;
        var parent = NestedSetParent<ValueKey>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(0, parent.Value.Value);
        Assert.NotNull(property.GetTypeMapping().Converter);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>An already nullable CLR key uses one nullable shape rather than nested Nullable types.</summary>
    /// <param name="present">Whether the live nullable parent contains zero.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredNullableClrKeyDoesNotNestNullableStorage(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var node = new NullableKeyNode
        {
            Id = 1,
            ParentId = 7,
        };

        var entry = context.Attach(node);
        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(nameof(NullableKeyNode.ParentId))!;

        // WHY: EF permits required keys with nullable CLR storage. Close the exact runtime type in test setup;
        // spelling int? against the public notnull annotation would introduce a compiler warning or suppression.
        var method =
            typeof(TypedPropertyReadTests).GetMethod(nameof(ProbeParent), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(int?));

        var read = method.CreateDelegate<Func<PropertyValues, IProperty, ParentProbe>>();

        // Act
        node.ParentId = present ? 0 : null;
        var parent = read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(present ? 0 : null, parent.Value);
        Assert.False(entry.Metadata.FindProperty(nameof(NullableKeyNode.Id))!.IsNullable);
        Assert.Equal(typeof(int?), property.ClrType);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Typed optional reads observe shadow values through the same live wrapper.</summary>
    /// <param name="present">Whether the updated shadow parent contains zero.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShadowParentReadsLiveSidecarChanges(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var entry = context.Attach(new ReadNode { Id = 1 });
        entry.Property<int?>(ReadContext.ShadowParent).CurrentValue = 7;
        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(ReadContext.ShadowParent)!;

        // Act
        entry.Property<int?>(ReadContext.ShadowParent).CurrentValue = present ? 0 : null;
        var parent = NestedSetParent<int>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(0, parent.Value);
        Assert.True(property.IsShadowProperty());
        Assert.Single(context.ChangeTracker.Entries<ReadNode>());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Named dictionary mappings read updated optional indexer values without another entry wrapper.</summary>
    /// <param name="present">Whether the updated indexer parent contains zero.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedSharedParentReadsLiveIndexerChanges(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var row = new Dictionary<string, object?>
        {
            [ReadContext.SharedKey] = 1,
            [ReadContext.SharedParent] = 7,
        };

        var entry = context
            .Set<Dictionary<string, object?>>(ReadContext.SharedEntity)
            .Attach(row);

        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(ReadContext.SharedParent)!;

        // Act
        row[ReadContext.SharedParent] = present ? 0 : null;
        var parent = NestedSetParent<int>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(0, parent.Value);
        Assert.Equal(ReadContext.SharedEntity, entry.Metadata.Name);
        Assert.True(property.IsIndexerProperty());
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Live reads observe generated temporary sidecars while the CLR identity stays zero.</summary>
    [Fact]
    public void TemporaryIdentityRemainsLiveOutsideClrStorage()
    {
        // Arrange
        using var context = new ReadContext();
        var node = new TemporaryKeyNode();
        var entry = context.Entry(node);
        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(nameof(TemporaryKeyNode.Id))!;

        // Act
        entry.State = EntityState.Added;
        var key = NestedSetTypedValue<int>.Read(values, property);

        // Assert
        Assert.True(entry.Property(value => value.Id).IsTemporary);
        Assert.NotEqual(0, key);
        Assert.Equal(0, node.Id);
        Assert.Equal(key, entry.Property(value => value.Id).CurrentValue);
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Warm multi-role reads have a fixed allocation budget instead of boxing each parent and bound.</summary>
    [Fact]
    public void RepeatedParentAndStructuralReadsAvoidPerReadAllocation()
    {
        // Arrange
        const int iterations = 100_000;
        using var context = new ReadContext();
        var node = new ReadNode
        {
            Id = 1,
            IntegerParent = 0,
            StructParent = new ValueKey(0),
            Left = 1,
            Right = 2,
            Depth = 3,
            Position = 4,
        };

        var entry = context.Attach(node);
        var values = entry.CurrentValues;
        var properties = new ReadProperties(entry.Metadata);
        _ = ReadSum(values, properties, 512);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = ReadSum(values, properties, iterations);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.Equal(11L * iterations, sum);
        Assert.InRange(allocated, 0L, 4096L);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Exercises exact nullable runtime keys without putting reflection inside a read loop.</summary>
    private static ParentProbe ProbeParent<TKey>(
        PropertyValues values,
        IProperty property
    )
        where TKey : notnull
    {
        var parent = NestedSetParent<TKey>.Read(values, property);

        return new ParentProbe(parent.HasValue, parent.BoxedValue);
    }

    /// <summary>Accumulates real typed reads so allocation checks cannot be optimized into unused calls.</summary>
    private static long ReadSum(
        PropertyValues values,
        ReadProperties properties,
        int iterations
    )
    {
        var sum = 0L;

        for (var index = 0; index < iterations; index++)
        {
            var integer = NestedSetParent<int>.Read(values, properties.IntegerParent);
            var converted = NestedSetParent<ValueKey>.Read(values, properties.StructParent);
            sum += integer.HasValue && converted.HasValue ? 1 : 0;
            sum += NestedSetTypedValue<long>.Read(values, properties.Left);
            sum += NestedSetTypedValue<long>.Read(values, properties.Right);
            sum += NestedSetTypedValue<int>.Read(values, properties.Depth);
            sum += NestedSetTypedValue<long>.Read(values, properties.Position);
        }

        return sum;
    }
}
