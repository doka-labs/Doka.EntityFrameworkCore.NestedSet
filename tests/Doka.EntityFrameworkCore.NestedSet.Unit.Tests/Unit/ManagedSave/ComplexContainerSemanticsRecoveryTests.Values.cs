namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexContainerSemanticsRecoveryTests
{
    /// <summary>Replays a struct whose Equals ignores an unmapped member the callback changed.</summary>
    [Fact]
    public async Task Restore_StructWithCustomEquals_IsReplayedWithoutConsultingIt()
    {
        // Arrange
        await using var context = new SemanticsContext();
        var payload = new EqualityPayload
        {
            Id = 3,
            Value = new EqualityValue
            {
                Key = 1,
                Note = 5,
            },
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        payload.Value = new EqualityValue
        {
            Key = 1,
            Note = 9,
        };

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(5, payload.Value.Note);
    }

    /// <summary>Restores an in-place primitive change inside a nullable struct, which EF cannot set directly.</summary>
    /// <param name="detectCallback">Whether EF observes the in-place change before recovery.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_NullableStruct_RestoresInPlacePrimitiveChange(
        bool detectCallback
    )
    {
        // Arrange
        await using var context = new SemanticsContext();
        var tags = new List<string> { "stored" };
        var payload = new NullablePayload
        {
            Id = 4,
            Value = new NullableValue
            {
                Count = 1,
                Tags = tags,
            },
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        tags.Add("callback");

        if (detectCallback)
        {
            context.ChangeTracker.DetectChanges();
        }

        // Act
        snapshot.Restore();

        // Assert
        Assert.Equal(["stored"], payload.Value!.Value.Tags);
        Assert.False(
            entry
                .ComplexProperty(nameof(NullablePayload.Value))
                .Property(nameof(NullableValue.Tags))
                .IsModified);
    }

    /// <summary>Restores a nullable struct scalar on an element hidden from the current CLR path.</summary>
    [Fact]
    public async Task Restore_HiddenElement_RestoresNullableStructThroughItsRetainedInstance()
    {
        // Arrange
        await using var context = new SemanticsContext();
        var row = new NullableRow
        {
            Value = new NullableValue
            {
                Count = 1,
                Tags = ["stored"],
            },
        };

        var payload = new NullableRowsPayload
        {
            Id = 5,
            Rows = [row],
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Rows.Clear();
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        row.Value!.Value.Tags!.Add("callback");
        payload.Rows.Add(
            new NullableRow
            {
                Value = new NullableValue
                {
                    Count = 2,
                    Tags = [],
                },
            });

        context.ChangeTracker.DetectChanges();
        payload.Rows.Clear();

        // Act
        snapshot.Restore();

        // Assert
        Assert.Empty(payload.Rows);
        Assert.Equal(["stored"], row.Value!.Value.Tags);
    }

    /// <summary>Restores scalars of a raw element that EF never registered, including its nested values.</summary>
    [Fact]
    public async Task Restore_UnregisteredElement_RestoresItsScalars()
    {
        // Arrange
        await using var context = new SemanticsContext();
        var raw = new NullableRow
        {
            Value = new NullableValue
            {
                Count = 1,
                Tags = ["stored"],
            },
        };

        var payload = new NullableRowsPayload
        {
            Id = 6,
            Rows = [],
        };

        context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Rows.Add(raw);
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        var tags = raw.Value.Value.Tags!;
        tags.Add("callback");
        raw.Value = new NullableValue
        {
            Count = 9,
            Tags = tags,
        };

        // Act
        snapshot.Restore();

        // Assert
        Assert.Same(raw, Assert.Single(payload.Rows));
        Assert.Equal(1, raw.Value!.Value.Count);
        Assert.Equal(["stored"], raw.Value!.Value.Tags);
    }
}
