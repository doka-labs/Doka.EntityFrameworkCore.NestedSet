namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexCollectionStorageRecoveryTests
{
    /// <summary>Copies restored primitive snapshots through containing structs without marking raw payload.</summary>
    /// <param name="replace">Whether the callback replaces primitive storage instead of mutating it in place.</param>
    /// <param name="detectCallback">Whether EF detects the callback before recovery.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NestedStructRestoresRawPrimitiveValuesThroughContainingPath(
        bool replace,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new StorageContext();
        var payload = new NestedStructPayload
        {
            Id = 7,
            Data = new OuterValue
            {
                Inner = new PrimitiveValue
                {
                    Number = 1,
                    Tags = ["Stored"],
                    Bytes = [1, 2],
                },
            },
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var outer = entry.ComplexProperty(nameof(NestedStructPayload.Data));
        var inner = outer.ComplexProperty(nameof(OuterValue.Inner));
        var numberProperty = inner.Property(nameof(PrimitiveValue.Number));
        var tagsProperty = inner.Property(nameof(PrimitiveValue.Tags));
        var bytesProperty = inner.Property(nameof(PrimitiveValue.Bytes));
        var pending = payload.Data;
        var pendingInner = pending.Inner;
        pendingInner.Number = 2;
        pendingInner.Tags.Add("Pending");
        pendingInner.Bytes[1] = 5;
        pending.Inner = pendingInner;
        payload.Data = pending;
        var number = Observe(numberProperty);
        var tags = Assert
            .IsType<List<string>>(tagsProperty.CurrentValue)
            .ToArray();

        var bytes = Assert
            .IsType<byte[]>(bytesProperty.CurrentValue)
            .ToArray();

        Assert.False(number.Modified);
        Assert.False(tagsProperty.IsModified);
        Assert.False(bytesProperty.IsModified);
        Assert.Equal(s_originalTags, Assert.IsType<List<string>>(tagsProperty.OriginalValue));
        Assert.Equal(s_originalBytes, Assert.IsType<byte[]>(bytesProperty.OriginalValue));
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var callback = payload.Data;
        var callbackInner = callback.Inner;
        callbackInner.Number = 99;

        if (replace)
        {
            callbackInner.Tags = ["Callback replacement"];
            callbackInner.Bytes = [99];
        }
        else
        {
            callbackInner.Tags.Clear();
            callbackInner.Bytes[0] = 99;
        }

        callback.Inner = callbackInner;
        payload.Data = callback;

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(number.Current, payload.Data.Inner.Number);
        AssertScalar(numberProperty, number);
        Assert.Equal(tags, payload.Data.Inner.Tags);
        Assert.Equal(tags, Assert.IsType<List<string>>(tagsProperty.CurrentValue));
        Assert.Equal(bytes, payload.Data.Inner.Bytes);
        Assert.Equal(bytes, Assert.IsType<byte[]>(bytesProperty.CurrentValue));
        Assert.Equal(s_originalTags, Assert.IsType<List<string>>(tagsProperty.OriginalValue));
        Assert.Equal(s_originalBytes, Assert.IsType<byte[]>(bytesProperty.OriginalValue));
        Assert.False(tagsProperty.IsModified);
        Assert.False(bytesProperty.IsModified);
        Assert.False(inner.IsModified);
        Assert.False(outer.IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }
}
