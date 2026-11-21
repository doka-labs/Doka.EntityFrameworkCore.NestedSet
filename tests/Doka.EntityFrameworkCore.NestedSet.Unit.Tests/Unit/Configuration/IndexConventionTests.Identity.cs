namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class IndexConventionTests
{
    /// <summary>A late scalar primary key completes the three structural tree-local indexes.</summary>
    /// <param name="engine">The provider whose normal model pipeline is exercised.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    public void FinalizeModel_LatePrimaryKey_CompletesStructuralIndexes(
        string engine
    )
    {
        // Arrange
        using var context = new ConventionContext(
            engine,
            model =>
            {
                var entity = model.Entity<IndexNode>();
                Configure(entity);
                entity.HasKey(node => node.Key);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        Assert.Equal(
            engine == "PostgreSql"
                ? 5
                : 4,
            entity
                .GetIndexes()
                .Count());

        Assert.All(
            entity
                .GetIndexes()
                .Where(index => index.Properties.Count > 2
                    && index
                        .GetFilter()
                        ?.EndsWith(" IS NULL", StringComparison.Ordinal)
                    != true),
            index => Assert.Contains(index.Properties, property => property.Name == nameof(IndexNode.TreeId)));

        Assert.All(entity.GetIndexes(), index => Assert.False(index.IsUnique));
    }

    /// <summary>An EF key replacement cannot silently invalidate the stable inferred NodeKey.</summary>
    /// <param name="engine">The provider whose normal model pipeline is exercised.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    public void FinalizeModel_ReplacedPrimaryKey_RejectsOrphanedNodeKey(
        string engine
    )
    {
        // Arrange
        using var context = new ConventionContext(
            engine,
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                Configure(entity);
                entity.HasKey(node => node.OtherKey);
            });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("primary or alternate key", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Finalization rejects an explicit nested-set key that disagrees with the eventual EF key.</summary>
    [Fact]
    public void FinalizeModel_ExplicitKeyMismatch_IsRejectedAtFinalization()
    {
        // Arrange
        using var context = new ConventionContext(
            "Sqlite",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                Configure(entity);
                entity.HasNestedSet(builder => builder.HasNodeKey(node => node.Key));
                entity.HasKey(node => node.OtherKey);
            });

        // Act
        var exception = Record.Exception(() => context.Model);

        // Assert
        var mismatch = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("primary or alternate key", mismatch.Message, StringComparison.Ordinal);
    }

    /// <summary>A scope-qualified composite key is a valid stable NodeKey identity.</summary>
    [Fact]
    public void FinalizeModel_CompositePrimaryKeyContainingScopeAndNodeKey_IsAccepted()
    {
        // Arrange
        using var context = new ConventionContext(
            "Sqlite",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                Configure(entity);
                entity.HasNestedSet(builder => builder.HasNodeKey(node => node.Key));
                entity.HasKey(node => new
                {
                    node.Scope,
                    node.Key,
                });
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        Assert.Equal(2, entity.FindPrimaryKey()!.Properties.Count);
        Assert.Equal(
            4,
            entity
                .GetIndexes()
                .Count());
    }

    /// <summary>A specialized unnamed index cannot suppress the ordinary path needed by structural queries.</summary>
    /// <param name="facet">The incompatible index facet to preserve.</param>
    [Theory]
    [InlineData("unique")]
    [InlineData("descending")]
    [InlineData("filter")]
    [InlineData("annotation")]
    public void FinalizeModel_SpecializedIndex_IsPreservedBesideOrdinaryAccessPath(
        string facet
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "Sqlite",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                var special = entity.HasIndex(node => new
                {
                    node.Scope,
                    node.TreeId,
                    node.Left,
                });

                Customize(special, facet);
                Configure(entity);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        var indexes = entity
            .GetIndexes()
            .Where(index => index.Properties.Count > 2 && index.Properties[2].Name == nameof(IndexNode.Left))
            .ToArray();

        Assert.Equal(2, indexes.Length);
        Assert.Equal(
            5,
            entity
                .GetIndexes()
                .Count());

        var original = Assert.Single(indexes, index => index.Name is null);
        var ordinary = Assert.Single(indexes, index => index.Name is not null);
        Assert.False(ordinary.IsUnique);
        Assert.Null(ordinary.IsDescending);
        Assert.All(ordinary.GetAnnotations(), annotation => Assert.Equal("Relational:Name", annotation.Name));
        Assert.True(
            original.IsUnique
            || original.IsDescending is not null
            || original
                .GetAnnotations()
                .Any());
    }

    /// <summary>Provider conventions still truncate generated names for long column and table names.</summary>
    /// <param name="engine">The provider whose identifier limit applies.</param>
    /// <param name="limit">The provider's maximum identifier length.</param>
    [Theory]
    [InlineData("MySql", 64)]
    [InlineData("MariaDb", 64)]
    [InlineData("PostgreSql", 63)]
    public void FinalizeModel_ProviderIdentifierLimits_ArePreserved(
        string engine,
        int limit
    )
    {
        // Arrange
        using var context = new ConventionContext(
            engine,
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                entity.ToTable("FolderHierarchyWithAnIntentionallyLongTableNameForIndexTests");
                entity
                    .Property(node => node.Scope)
                    .HasColumnName("TenantScopeWithALongDatabaseColumnName");

                entity
                    .Property(node => node.Key)
                    .HasColumnName("NodePrimaryKeyWithALongDatabaseColumnName");

                Configure(entity);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        // Assert
        Assert.Equal(
            engine == "PostgreSql"
                ? 5
                : 4,
            entity
                .GetIndexes()
                .Count());

        var names = entity
            .GetIndexes()
            .Select(index => index.GetDatabaseName()!)
            .ToArray();

        Assert.All(names, name => Assert.InRange(name.Length, 1, limit));
        Assert.Equal(
            engine == "PostgreSql"
                ? 5
                : 4,
            names
                .Distinct(StringComparer.Ordinal)
                .Count());
    }
}
