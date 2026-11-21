using System.Collections.ObjectModel;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks that recovery keeps caller collection code, Equals and nullable structs out of its path.</summary>
public sealed partial class ComplexContainerSemanticsRecoveryTests
{
    /// <summary>Stages a field-mapped validating collection without ever calling its mutators.</summary>
    /// <param name="detectAfterRemoval">Whether EF observes the raw removal before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_FieldMappedCollection_IsNeverMutatedByStaging(
        bool detectAfterRemoval
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var rows = new GuardedRows { first };
        var payload = new GuardedPayload
        {
            Id = 1,
            Rows = rows,
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var added = new Row { Text = "callback" };
        rows.Add(added);
        context.ChangeTracker.DetectChanges();
        rows.Remove(added);

        if (detectAfterRemoval)
        {
            context.ChangeTracker.DetectChanges();
        }

        rows.Mutations = 0;

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(rows, payload.Rows);
        Assert.Same(first, Assert.Single(rows));
        Assert.Equal(0, rows.Mutations);
        Assert.Equal(
            EntityState.Unchanged,
            entry
                .ComplexCollection(nameof(GuardedPayload.Rows))[0].State);
    }

    /// <summary>Observes no temporary membership through CollectionChanged during field-mapped staging.</summary>
    [Fact]
    public async Task Restore_FieldMappedObservableCollection_RaisesOnlyRestoringEvents()
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var rows = new ObservableCollection<Row> { first };
        var payload = new ObservablePayload
        {
            Id = 2,
            Rows = rows,
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var added = new Row { Text = "callback" };
        rows.Add(added);
        context.ChangeTracker.DetectChanges();
        rows.Remove(added);
        List<Row?> observed = [];
        rows.CollectionChanged += (_, change) =>
        {
            if (change.NewItems is not null)
            {
                observed.AddRange(change.NewItems.Cast<Row?>());
            }
        };

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(first, Assert.Single(rows));
        Assert.DoesNotContain(null, observed);
        Assert.DoesNotContain(added, observed);
    }

    /// <summary>Stages a property-mapped validating collection without ever handing it a null slot.</summary>
    /// <param name="keepFirst">Whether an existing element can fill a vacant slot.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restore_PropertyMappedCollection_NeverReceivesNull(
        bool keepFirst
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var rows = new GuardedRows();

        if (keepFirst)
        {
            rows.Add(first);
        }

        var payload = new PropertyGuardedPayload
        {
            Id = 7,
            Rows = rows,
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var added = new Row { Text = "callback" };
        rows.Add(added);
        context.ChangeTracker.DetectChanges();
        rows.Remove(added);

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(rows, payload.Rows);
        Assert.Equal(
            keepFirst
                ? [first]
                : [],
            rows.ToArray());
    }

    /// <summary>Installs exact membership when a callback changed a read-only view's hidden backing list.</summary>
    /// <param name="detectCallback">Whether EF observes the change before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ReadOnlyViewWithChangedBacking_RestoresMembership(
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var backing = new List<Row> { first };
        var payload = new ViewPayload
        {
            Id = 8,
            Rows = new ReadOnlyCollection<Row>(backing),
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        backing.Clear();
        backing.Add(new Row { Text = "callback" });

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(first, Assert.Single(payload.Rows));
        Assert.Equal(
            EntityState.Unchanged,
            entry
                .ComplexCollection(nameof(ViewPayload.Rows))[0].State);
    }

    /// <summary>Accepts the setter's copy when a callback replaced a collection behind a copying setter.</summary>
    /// <param name="detectCallback">Whether EF observes the replacement before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_CopyingSetter_RestoresMembershipAfterReplacement(
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var first = new Row { Text = "first" };
        var payload = new CopyingPayload
        {
            Id = 9,
            Rows = [first],
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Rows = [new Row { Text = "callback" }];

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(first, Assert.Single(payload.Rows));
        Assert.Equal(
            EntityState.Unchanged,
            entry
                .ComplexCollection(nameof(CopyingPayload.Rows))[0].State);
    }
}
