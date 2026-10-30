namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
    /// <summary>Absent generated indexer entries remain safe through ordinary EF tracking and change detection.</summary>
    [Fact]
    public void MissingGeneratedBagEntryUsesNaturalSentinel()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
            ConfigureBag(model).HasNestedSet(builder => builder.HasAncestorIndex("Branch")));

        var row = new Dictionary<string, object>
        {
            ["NodeId"] = 1,
            ["TreeIdentity"] = Guid.NewGuid(),
            ["L"] = 1L,
            ["R"] = 2L,
            ["D"] = 0,
            ["P"] = 0L,
        };

        // Act
        var entry = context.Set<Dictionary<string, object>>("NamedHierarchy").Add(row);
        context.ChangeTracker.DetectChanges();

        // Assert
        var flag = entry.Property<bool>("Branch");
        Assert.False(flag.CurrentValue);
        Assert.False(flag.Metadata.IsNullable);
        Assert.Equal(typeof(bool), flag.Metadata.ClrType);
        Assert.Null(flag.Metadata.Sentinel);
        Assert.False(row.ContainsKey("Branch"));
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>An application sentinel cannot silently invalidate missing-key access for a generated Boolean indexer.</summary>
    [Fact]
    public void ExplicitNonNullBagSentinelIsRejected()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = ConfigureBag(model);
            entity.Property<bool>("Branch").HasSentinel(false);
            entity.HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains("natural null missing-key sentinel", invalid.Message, StringComparison.Ordinal);
    }
}
