namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TypedPropertyReadTests
{
    /// <summary>Typed optional reads observe shadow values through the same live wrapper.</summary>
    /// <param name="present">Whether the updated shadow parent contains zero.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_ShadowParent_ReadsLiveSidecarChanges(
        bool present
    )
    {
        // Arrange
        using var context = new ReadContext();
        var entry = context.Attach(new ReadNode { Id = 1 });
        entry.Property<int?>(ReadContext.ShadowParent)
            .CurrentValue = 7;

        var values = entry.CurrentValues;
        var property = entry.Metadata.FindProperty(ReadContext.ShadowParent)!;

        // Act
        entry.Property<int?>(ReadContext.ShadowParent)
            .CurrentValue = present
            ? 0
            : null;

        var parent = NestedSetParent<int>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(0, parent.Value);
        Assert.True(property.IsShadowProperty());
        Assert.Single(context.ChangeTracker.Entries<ReadNode>());
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Named dictionary mappings read updated optional indexer values without another entry wrapper.</summary>
    /// <param name="present">Whether the updated indexer parent contains zero.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_NamedSharedParent_ReadsLiveIndexerChanges(
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
        row[ReadContext.SharedParent] = present
            ? 0
            : null;

        var parent = NestedSetParent<int>.Read(values, property);

        // Assert
        Assert.Equal(present, parent.HasValue);
        Assert.Equal(0, parent.Value);
        Assert.Equal(ReadContext.SharedEntity, entry.Metadata.Name);
        Assert.True(property.IsIndexerProperty());
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Live reads observe generated temporary sidecars while the CLR identity stays zero.</summary>
    [Fact]
    public void Read_TemporaryIdentity_RemainsLiveOutsideClrStorage()
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
        Assert.True(
            entry.Property(value => value.Id)
                .IsTemporary);

        Assert.NotEqual(0, key);
        Assert.Equal(0, node.Id);
        Assert.Equal(
            key,
            entry.Property(value => value.Id)
                .CurrentValue);

        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
