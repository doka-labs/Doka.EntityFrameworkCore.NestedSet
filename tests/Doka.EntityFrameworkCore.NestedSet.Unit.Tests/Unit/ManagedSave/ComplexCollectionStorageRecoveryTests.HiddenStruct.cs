namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexCollectionStorageRecoveryTests
{
    /// <summary>Restores hidden original rows through actual struct storage instead of verifying stale boxes.</summary>
    /// <param name="clear">Whether the captured raw edit hides every row instead of only the trailing row.</param>
    /// <param name="detectCallback">Whether EF detects the callback's replacement collection.</param>
    /// <param name="acceptCallback">Whether EF accepts detected callback changes before recovery.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task HiddenOriginalStructRestoresPrimitiveValuesAndNativeIntent(
        bool clear,
        bool detectCallback,
        bool acceptCallback
    )
    {
        // Arrange
        await using var context = new StorageContext();
        var first = new StructRow
        {
            Data = new PrimitiveValue
            {
                Number = 3,
                Tags = ["First"],
                Bytes = [3, 4],
            },
        };

        var hidden = new StructRow
        {
            Data = new PrimitiveValue
            {
                Number = 1,
                Tags = ["Stored"],
                Bytes = [1, 2],
            },
        };

        var payload = new StructRowsPayload
        {
            Id = 7,
            Rows = [first, hidden],
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var rows = payload.Rows;
        var collection = entry.ComplexCollection(nameof(StructRowsPayload.Rows));
        var hiddenEntry = collection[1];
        var hiddenComplex = hiddenEntry.ComplexProperty(nameof(StructRow.Data));
        var numberProperty = hiddenComplex.Property(nameof(PrimitiveValue.Number));
        var tagsProperty = hiddenComplex.Property(nameof(PrimitiveValue.Tags));
        var bytesProperty = hiddenComplex.Property(nameof(PrimitiveValue.Bytes));
        var pending = hidden.Data;
        pending.Number = 2;
        pending.Tags.Add("Pending");
        pending.Bytes[1] = 5;
        hidden.Data = pending;
        numberProperty.IsModified = true;
        tagsProperty.IsModified = true;
        bytesProperty.IsModified = true;
        var numberOriginal = numberProperty.OriginalValue;
        var tagsOriginal = Assert
            .IsType<List<string>>(tagsProperty.OriginalValue)
            .ToArray();

        var bytesOriginal = Assert
            .IsType<byte[]>(bytesProperty.OriginalValue)
            .ToArray();

        var expectedTags = hidden.Data.Tags.ToArray();
        var expectedBytes = hidden.Data.Bytes.ToArray();
        var entityState = entry.State;
        var hiddenState = hiddenEntry.State;

        if (clear)
        {
            rows.Clear();
        }
        else
        {
            rows.RemoveAt(1);
        }

        var modified = collection.IsModified;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var callback = hidden.Data;
        callback.Number = 99;
        callback.Tags.Clear();
        callback.Bytes[0] = 99;
        hidden.Data = callback;
        payload.Rows =
        [
            new StructRow
            {
                Data = new PrimitiveValue
                {
                    Number = 99,
                    Tags = ["Callback"],
                    Bytes = [99],
                },
            },
        ];

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        if (acceptCallback)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(rows, payload.Rows);
        Assert.Equal(clear ? 0 : 1, rows.Count);

        if (!clear)
        {
            Assert.Same(first, rows[0]);
        }

        Assert.Equal(entityState, entry.State);
        Assert.Equal(hiddenState, hiddenEntry.State);
        Assert.Equal(modified, collection.IsModified);
        Assert.Equal(2, hidden.Data.Number);
        Assert.Equal(expectedTags, hidden.Data.Tags);
        Assert.Equal(expectedBytes, hidden.Data.Bytes);
        Assert.Equal(numberOriginal, numberProperty.OriginalValue);
        Assert.Equal(tagsOriginal, Assert.IsType<List<string>>(tagsProperty.OriginalValue));
        Assert.Equal(bytesOriginal, Assert.IsType<byte[]>(bytesProperty.OriginalValue));
        Assert.True(numberProperty.IsModified);
        Assert.True(tagsProperty.IsModified);
        Assert.True(bytesProperty.IsModified);
        Assert.True(hiddenComplex.IsModified);

        var restoredOriginal = collection
            .GetOriginalEntry(1)
            .ComplexProperty(nameof(StructRow.Data));

        Assert.Equal(numberOriginal,
            restoredOriginal.Property(nameof(PrimitiveValue.Number)).OriginalValue);
        Assert.True(restoredOriginal.Property(nameof(PrimitiveValue.Tags)).IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }
}
