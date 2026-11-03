namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexCollectionStorageRecoveryTests
{
    private static readonly string[] s_originalTags = ["Stored"];
    private static readonly byte[] s_originalBytes = [1, 2];

    /// <summary>Returns in-place and replacement callbacks against raw or detected pending scalars.</summary>
    public static TheoryData<string, bool, bool> PrimitiveCases
    {
        get
        {
            var cases = new TheoryData<string, bool, bool>();

            foreach (var mutation in new[]
                     {
                         "TagsAdd",
                         "TagsClear",
                         "TagsReplace",
                         "BytesEdit",
                         "BytesReplace",
                     })
            {
                cases.Add(mutation, false, false);
                cases.Add(mutation, false, true);
                cases.Add(mutation, true, false);
                cases.Add(mutation, true, true);
            }

            return cases;
        }
    }

    /// <summary>Prevents shallow struct replay from overwriting comparer-restored primitive scalar snapshots.</summary>
    /// <param name="mutation">The callback operation applied to a primitive collection or byte-array scalar.</param>
    /// <param name="detectPending">Whether pending scalar changes are explicitly marked before capture.</param>
    /// <param name="detectCallback">Whether EF observes callback scalar changes before recovery.</param>
    [Theory]
    [MemberData(nameof(PrimitiveCases))]
    public async Task StructPrimitiveValuesRestoreComparerSnapshotsAndFlags(
        string mutation,
        bool detectPending,
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new StorageContext();
        var payload = new StructPayload
        {
            Id = 7,
            Data = new PrimitiveValue
            {
                Number = 1,
                Tags = ["Stored"],
                Bytes = [1, 2],
            },
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var complex = entry.ComplexProperty(nameof(StructPayload.Data));
        var tagsProperty = complex.Property(nameof(PrimitiveValue.Tags));
        var bytesProperty = complex.Property(nameof(PrimitiveValue.Bytes));
        var value = payload.Data;
        value.Number = 2;
        value.Tags.Add("Pending");
        value.Bytes[1] = 5;
        payload.Data = value;

        if (detectPending)
        {
            context.ChangeTracker.DetectChanges();
        }

        var number = Observe(complex.Property(nameof(PrimitiveValue.Number)));
        var tags = Assert
            .IsType<List<string>>(tagsProperty.CurrentValue)
            .ToArray();

        var originalTags = Assert
            .IsType<List<string>>(tagsProperty.OriginalValue)
            .ToArray();

        var bytes = Assert
            .IsType<byte[]>(bytesProperty.CurrentValue)
            .ToArray();

        var originalBytes = Assert
            .IsType<byte[]>(bytesProperty.OriginalValue)
            .ToArray();

        var tagsModified = tagsProperty.IsModified;
        var bytesModified = bytesProperty.IsModified;
        var complexModified = complex.IsModified;
        var entityState = entry.State;
        Assert.NotSame(payload.Data.Tags, tagsProperty.OriginalValue);
        Assert.NotSame(payload.Data.Bytes, bytesProperty.OriginalValue);
        Assert.Equal(s_originalTags, originalTags);
        Assert.Equal(s_originalBytes, originalBytes);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var callback = payload.Data;
        callback.Number = 99;

        switch (mutation)
        {
            case "TagsAdd":
                callback.Tags.Add("Callback");
                break;
            case "TagsClear":
                callback.Tags.Clear();
                break;
            case "TagsReplace":
                callback.Tags = ["Callback replacement"];
                break;
            case "BytesEdit":
                callback.Bytes[0] = 99;
                break;
            case "BytesReplace":
                callback.Bytes = [99];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        payload.Data = callback;

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.True(complex.Metadata.ComplexType.ClrType.IsValueType);
        Assert.False(complex.Metadata.IsCollection);
        Assert.Equal(number.Current, payload.Data.Number);
        AssertScalar(complex.Property(nameof(PrimitiveValue.Number)), number);
        Assert.Equal(tags, payload.Data.Tags);
        Assert.Equal(tags, Assert.IsType<List<string>>(tagsProperty.CurrentValue));
        Assert.Equal(originalTags, Assert.IsType<List<string>>(tagsProperty.OriginalValue));
        Assert.Equal(bytes, payload.Data.Bytes);
        Assert.Equal(bytes, Assert.IsType<byte[]>(bytesProperty.CurrentValue));
        Assert.Equal(originalBytes, Assert.IsType<byte[]>(bytesProperty.OriginalValue));
        Assert.Equal(tagsModified, tagsProperty.IsModified);
        Assert.Equal(bytesModified, bytesProperty.IsModified);
        Assert.Equal(complexModified, complex.IsModified);
        Assert.Equal(entityState, entry.State);
        Assert.False(entry.Property(value => value.Id).IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }
}
