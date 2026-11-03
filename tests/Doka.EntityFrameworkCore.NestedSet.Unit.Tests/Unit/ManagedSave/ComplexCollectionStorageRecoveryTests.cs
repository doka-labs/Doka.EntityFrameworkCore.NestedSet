using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks exact complex container recovery independently of the caller's collection implementation.</summary>
public sealed partial class ComplexCollectionStorageRecoveryTests
{
    /// <summary>Returns immutable and mutable storage controls with independent callback/detection variants.</summary>
    public static TheoryData<string, string, bool> CollectionCases
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();

            foreach (var storage in new[] { "List", "Array", "ReadOnly", "Immutable" })
            {
                foreach (var callback in new[] { "None", "Replace", "Scalar" })
                {
                    cases.Add(storage, callback, false);
                    cases.Add(storage, callback, true);
                }
            }

            return cases;
        }
    }

    /// <summary>Restores EF state without writing untouched read-only slots or replacing caller containers.</summary>
    /// <param name="storage">The runtime collection implementation declared through the same mapped interface.</param>
    /// <param name="callback">Whether the callback preserves storage, replaces it or edits payload.</param>
    /// <param name="detectCallback">Whether EF observes the callback before recovery.</param>
    [Theory]
    [MemberData(nameof(CollectionCases))]
    public async Task RuntimeStoragePreservesContainerAndScalarState(
        string storage,
        string callback,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new StorageContext();
        var first = new StorageItem { Text = "Stored first" };
        var last = new StorageItem { Text = "Stored last" };
        StorageItem[] items = [first, last];
        IList<StorageItem> rows = storage switch
        {
            "List" => new List<StorageItem>(items),
            "Array" => items,
            "ReadOnly" => new ReadOnlyCollection<StorageItem>(items),
            "Immutable" => ImmutableList.CreateRange(items),
            _ => throw new ArgumentOutOfRangeException(nameof(storage)),
        };

        var payload = new CollectionPayload
        {
            Id = 7,
            Rows = rows,
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var collection = entry.ComplexCollection(nameof(CollectionPayload.Rows));
        payload.Marker = "Pending marker";
        entry.Property(value => value.Marker).IsModified = true;
        first.Text = "Pending first";
        collection[0].Property(nameof(StorageItem.Text)).IsModified = true;

        var marker = Observe(entry.Property(value => value.Marker));
        var firstText = Observe(collection[0].Property(nameof(StorageItem.Text)));

        var lastText = Observe(collection[1].Property(nameof(StorageItem.Text)));

        var state = entry.State;
        var modified = collection.IsModified;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        if (callback == "Replace")
        {
            payload.Rows = [new StorageItem { Text = "Callback replacement" }];
        }
        else if (callback == "Scalar")
        {
            first.Text = "Callback scalar";
            payload.Marker = "Callback marker";
        }

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(typeof(IList<StorageItem>), collection.Metadata.ClrType);
        Assert.Equal(storage != "List", rows.IsReadOnly);
        Assert.Same(rows, payload.Rows);
        Assert.Equal(2, rows.Count);
        Assert.Same(first, rows[0]);
        Assert.Same(last, rows[1]);
        Assert.Equal(state, entry.State);
        Assert.Equal(modified, collection.IsModified);
        AssertScalar(entry.Property(value => value.Marker), marker);
        AssertScalar(collection[0].Property(nameof(StorageItem.Text)),
            firstText);
        AssertScalar(collection[1].Property(nameof(StorageItem.Text)),
            lastText);
        Assert.Equal("Pending first", rows[0].Text);
        Assert.Equal("Stored last", rows[1].Text);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Preserves copying-setter storage without invoking an unnecessary setter.</summary>
    /// <param name="mutation">Whether a callback preserves, shrinks or grows the original mutable container.</param>
    /// <param name="detectCallback">Whether EF observes the callback before recovery.</param>
    [Theory]
    [InlineData("None", false)]
    [InlineData("None", true)]
    [InlineData("Remove", false)]
    [InlineData("Remove", true)]
    [InlineData("Add", false)]
    [InlineData("Add", true)]
    public async Task CopyingSetterIsNotCalledForCapturedContainer(
        string mutation,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new StorageContext();
        var first = new StorageItem { Text = "Stored first" };
        var last = new StorageItem { Text = "Stored last" };
        var payload = new CopyingPayload
        {
            Id = 7,
            Rows = [first, last],
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var rows = payload.Rows;
        var collection = entry.ComplexCollection(nameof(CopyingPayload.Rows));
        first.Text = "Pending first";
        collection[0].Property(nameof(StorageItem.Text)).IsModified = true;

        var firstText = Observe(collection[0].Property(nameof(StorageItem.Text)));

        var lastText = Observe(collection[1].Property(nameof(StorageItem.Text)));

        var setterCalls = payload.SetterCalls;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        if (mutation == "Remove")
        {
            rows.RemoveAt(0);
        }
        else if (mutation == "Add")
        {
            rows.Add(new StorageItem { Text = "Callback addition" });
        }

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(PropertyAccessMode.Property, collection.Metadata.GetPropertyAccessMode());
        Assert.Same(rows, payload.Rows);
        Assert.Equal(setterCalls, payload.SetterCalls);
        Assert.Equal(2, rows.Count);
        Assert.Same(first, rows[0]);
        Assert.Same(last, rows[1]);
        AssertScalar(collection[0].Property(nameof(StorageItem.Text)),
            firstText);
        AssertScalar(collection[1].Property(nameof(StorageItem.Text)),
            lastText);
        Assert.Equal("Pending first", rows[0].Text);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }
}
