using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InsertionRefreshAllocationTests
{
    /// <summary>A null current key is rejected before a custom comparer requiring non-null operands runs.</summary>
    [Fact]
    public void CustomComparerNullKeyIsRejectedWithoutInvokingEquality()
    {
        // Arrange
        using var context = new AllocationContext();
        var entry = context.Add(new StringNode { Id = "one" });
        var identity = NestedSetInsertionTracking.Capture(entry);
        var property = entry.Metadata.FindPrimaryKey()!.Properties[0];
        entry.Entity.Id = null;

        // Act
        var matches = identity.Matches(property);

        // Assert
        Assert.False(matches);
    }

    /// <summary>A removed non-nullable bag key retains the native sentinel conversion diagnostic.</summary>
    [Fact]
    public void MissingIndexerKeyPreservesTheFrameworkGetterDiagnostic()
    {
        // Arrange
        using var context = new AllocationContext();
        var node = new Dictionary<string, object> { ["Id"] = 1 };
        var entry = context
            .Set<Dictionary<string, object>>("IndexerNode")
            .Add(node);

        var property = entry.Metadata.FindPrimaryKey()!.Properties[0];
        var identity = NestedSetInsertionTracking.Capture(entry);
        node.Remove("Id");
        var update = NestedSetInsertionTracking.EntryIdentity(entry);
        // WHY: EF's object getter cannot unbox a missing int member with a non-null custom sentinel.
        // Preserve that framework boundary rather than inventing a default identity for the callback.
        var frameworkError = Record.Exception(() => update.GetCurrentValue(property));

        // Act
        var error = Record.Exception(() => identity.Matches(property));

        // Assert
        Assert.IsType<NullReferenceException>(frameworkError);
        Assert.IsType<NullReferenceException>(error);
        Assert.False(node.ContainsKey("Id"));
    }

    /// <summary>A generated sidecar does not conceal an independently changed Added CLR key.</summary>
    /// <param name="replacement">The CLR key written while the generated value still belongs to EF's sidecar.</param>
    [Theory]
    [InlineData(37)]
    [InlineData(99)]
    public void GeneratedSidecarRejectsChangedClrSentinel(
        int replacement
    )
    {
        // Arrange
        using var context = new AllocationContext();
        var entry = context.Add(new GeneratedNode());
        var property = entry.Metadata.FindProperty(nameof(GeneratedNode.Id))!;
        var identity = NestedSetInsertionTracking.Capture(entry);
        NestedSetInsertionTracking
            .EntryIdentity(entry)
            .SetStoreGeneratedValue(property, 37, setModified: false);

        identity.Refresh();
        entry.Entity.Id = replacement;

        // Act
        var matches = identity.Matches(property);

        // Assert
        Assert.False(matches);
        Assert.Equal(37, entry.Property(property).CurrentValue);
        Assert.Equal(replacement, entry.Entity.Id);
    }

    /// <summary>EF acceptance copies the provider key into CLR without invalidating the captured identity.</summary>
    [Fact]
    public void AcceptedGeneratedIdentityMatchesItsCopiedClrValue()
    {
        // Arrange
        using var context = new AllocationContext();
        var entry = context.Add(new GeneratedNode());
        var property = entry.Metadata.FindProperty(nameof(GeneratedNode.Id))!;
        var identity = NestedSetInsertionTracking.Capture(entry);
        NestedSetInsertionTracking
            .EntryIdentity(entry)
            .SetStoreGeneratedValue(property, 37, setModified: false);

        identity.Refresh();

        // Act
        context.ChangeTracker.AcceptAllChanges();
        var matches = identity.Matches(property);

        // Assert
        Assert.True(matches);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(37, entry.Entity.Id);
    }

    /// <summary>Each composite-key component participates in callback identity validation.</summary>
    /// <param name="component">The name of the independently changed composite-key component.</param>
    [Theory]
    [InlineData(nameof(CompoundNode.Scope))]
    [InlineData(nameof(CompoundNode.Id))]
    public void CompositeIdentityRejectsEitherChangedComponent(
        string component
    )
    {
        // Arrange
        using var context = new AllocationContext();
        var node = new CompoundNode
        {
            Scope = 1,
            Id = 2,
        };

        var entry = context.Add(node);
        var identity = NestedSetInsertionTracking.Capture(entry);
        var property = entry.Metadata.FindProperty(component)!;

        // Act
        if (component == nameof(CompoundNode.Scope))
        {
            node.Scope = 3;
        }
        else
        {
            node.Id = 3;
        }

        var matches = identity.Matches(property);

        // Assert
        Assert.False(matches);
    }

    /// <summary>
    /// Returning CLR to its staged key does not hide an intermediate installed relationship identity.
    /// </summary>
    [Fact]
    public void IntermediateRelationshipIdentityIsRemovedWhenClrReturnsToStagedKey()
    {
        // Arrange
        using var context = new AllocationContext();
        var unrelated = context.Add(new IntNode { Id = 10 });
        var entry = context.Add(new IntNode { Id = 1 });
        var identity = NestedSetInsertionTracking.Capture(entry);
        var key = entry.Metadata.FindPrimaryKey()!;
        entry.Entity.Id = 2;
        context.ChangeTracker.DetectChanges();
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var installed = updates.TryGetEntry(key, [2]);
        var introduced = NestedSetInsertionTracking.EntryIdentity(entry);
        var retained = NestedSetInsertionTracking.EntryIdentity(unrelated);
        entry.Entity.Id = 1;

        // Act
        identity.PrepareDetach();
        identity.Detach();

        // Assert
        Assert.Same(introduced, installed);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Null(updates.TryGetEntry(key, [1]));
        Assert.Null(updates.TryGetEntry(key, [2]));
        Assert.Same(retained, updates.TryGetEntry(key, [10]));
        Assert.Equal(1, entry.Entity.Id);
    }

    /// <summary>Detaching an introduced dependent preserves the other dependent's exact principal bucket.</summary>
    [Fact]
    public void DependentCleanupPreservesUnrelatedMembership()
    {
        // Arrange
        using var context = new AllocationContext();
        context.Add(new ForeignNode { Id = 1 });
        var unrelated = context.Add(
            new ForeignNode
            {
                Id = 2,
                ParentId = 1,
            });

        var entry = context.Add(
            new ForeignNode
            {
                Id = 3,
                ParentId = 1,
            });

        var identity = NestedSetInsertionTracking.Capture(entry);
        var key = entry.Metadata.FindPrimaryKey()!;
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var retained = NestedSetInsertionTracking.EntryIdentity(unrelated);

        // Act
        identity.PrepareDetach();
        identity.Detach();

        // Assert
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Null(updates.TryGetEntry(key, [3]));
        Assert.Same(retained, updates.TryGetEntry(key, [2]));
        Assert.Equal(1, unrelated.Entity.ParentId);
        var foreignKey = Assert.Single(entry.Metadata.GetForeignKeys());
        var nativeMap = NestedSetInsertionContract.Instance.FindMap(
            NestedSetInsertionContract.Instance.Manager(retained),
            key)!;

        var dependents = NestedSetInsertionContract
            .Instance
            .Map(nativeMap.GetType())
            .Dependents(nativeMap, foreignKey)!;

        var bucket = NestedSetInsertionContract.Instance.Dependents(dependents, [1]);
        var introduced = NestedSetInsertionTracking.EntryIdentity(entry);
        Assert.Single(bucket, candidate => ReferenceEquals(candidate, retained));
        Assert.DoesNotContain(bucket, candidate => ReferenceEquals(candidate, introduced));
    }
}
