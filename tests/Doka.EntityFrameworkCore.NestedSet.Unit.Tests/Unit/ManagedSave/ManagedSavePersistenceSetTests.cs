using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves persistence authorization while sharing its exact sets with relationship completion.</summary>
public sealed class ManagedSavePersistenceSetTests
{
    /// <summary>No save result can be inferred from an entered scope before its native validation boundary.</summary>
    [Fact]
    public async Task PersistenceSetsRejectReadsBeforeTheNativeBoundary()
    {
        // Arrange
        await using var context = CreateContext();
        using var scope = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });

        // Act
        var entries = Record.Exception(() => scope.PersistedEntries);
        var entities = Record.Exception(() => scope.PersistedEntities);
        var persisted = Record.Exception(scope.RequirePersisted);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(entries)
                .Code);

        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(entities)
                .Code);

        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(persisted)
                .Code);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>The successful boundary publishes exactly the native identity and object already validated.</summary>
    [Fact]
    public async Task PersistenceSetsRetainTheValidatedNativeAndObjectIdentity()
    {
        // Arrange
        await using var context = CreateContext();
        var node = CreateNode();
        var entry = context.Add(node);
        var native = NestedSetInsertionContract.Instance.Entry(entry);
        using var scope = NestedSetSaveChanges.EnterManagedSave(context, [node], static () => { });

        // Act
        NestedSetSaveChanges.ValidateManagedPersistence(context, [native]);
        var entries = scope.PersistedEntries;
        var entities = scope.PersistedEntities;

        // Assert
        Assert.Same(native, Assert.Single(entries));
        Assert.Same(node, Assert.Single(entities));
        Assert.Same(entries, scope.PersistedEntries);
        Assert.Same(entities, scope.PersistedEntities);
        Assert.Equal(EntityState.Added, entry.State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A nested save starts unauthorized and restores the exact outer sets on disposal.</summary>
    [Fact]
    public async Task NestedScopeRestoresTheExactOuterPersistenceSets()
    {
        // Arrange
        await using var context = CreateContext();
        using var outer = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });
        NestedSetSaveChanges.ValidateManagedPersistence(context, []);
        var outerEntries = outer.PersistedEntries;
        var outerEntities = outer.PersistedEntities;
        IReadOnlySet<IUpdateEntry> innerEntries;
        IReadOnlySet<object> innerEntities;
        Exception? beforeInnerBoundary;

        // Act
        using (var inner = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { }))
        {
            beforeInnerBoundary = Record.Exception(() => inner.PersistedEntries);
            NestedSetSaveChanges.ValidateManagedPersistence(context, []);
            innerEntries = inner.PersistedEntries;
            innerEntities = inner.PersistedEntities;
        }

        var restoredEntries = outer.PersistedEntries;
        var restoredEntities = outer.PersistedEntities;

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(beforeInnerBoundary)
                .Code);

        Assert.NotSame(outerEntries, innerEntries);
        Assert.NotSame(outerEntities, innerEntities);
        Assert.Same(outerEntries, restoredEntries);
        Assert.Same(outerEntities, restoredEntities);
        Assert.Empty(restoredEntries);
        Assert.Empty(restoredEntities);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Disposal keeps the authorization sets unavailable even after a successful native boundary.</summary>
    [Fact]
    public async Task DisposedScopeCannotExposePersistenceSets()
    {
        // Arrange
        await using var context = CreateContext();
        using var scope = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });
        NestedSetSaveChanges.ValidateManagedPersistence(context, []);

        // Act
        scope.Dispose();
        var entries = Record.Exception(() => scope.PersistedEntries);
        var entities = Record.Exception(() => scope.PersistedEntities);

        // Assert
        Assert.IsType<ObjectDisposedException>(entries);
        Assert.IsType<ObjectDisposedException>(entities);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>A missing planned write never publishes a partial native insertion set.</summary>
    [Fact]
    public async Task RejectedNativeWriteSetCannotPublishPersistenceSets()
    {
        // Arrange
        await using var context = CreateContext();
        var node = CreateNode();
        var entry = context.Add(node);
        using var scope = NestedSetSaveChanges.EnterManagedSave(context, [node], static () => { });

        // Act
        var validation = Record.Exception(() => NestedSetSaveChanges.ValidateManagedPersistence(context, []));
        var entries = Record.Exception(() => scope.PersistedEntries);
        var entities = Record.Exception(() => scope.PersistedEntities);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(validation)
                .Code);

        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(entries)
                .Code);

        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(entities)
                .Code);

        Assert.Equal(EntityState.Added, entry.State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Creates the existing hierarchy fixture with its real persistence guard and manual detection.</summary>
    private static TreeContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

        return new TreeContext(options)
        {
            ChangeTracker = { AutoDetectChangesEnabled = false },
        };
    }

    /// <summary>Creates one valid pending hierarchy row without issuing a database command.</summary>
    private static TreeNode CreateNode() => new()
    {
        NodeId = 11,
        Tree = 7,
        TreeId = Guid.NewGuid(),
        Start = 1,
        End = 2,
    };
}
