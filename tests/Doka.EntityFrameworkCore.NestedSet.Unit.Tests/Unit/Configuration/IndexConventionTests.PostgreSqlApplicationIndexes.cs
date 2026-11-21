namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class IndexConventionTests
{
    /// <summary>Application-created or adopted parent indexes retain their complete configured metadata.</summary>
    /// <param name="facet">The explicit index setting that the convention must preserve.</param>
    [Theory]
    [InlineData("unnamed")]
    [InlineData("named")]
    [InlineData("filter")]
    [InlineData("unfiltered")]
    [InlineData("method")]
    [InlineData("annotation-filter")]
    [InlineData("database-name")]
    [InlineData("unique")]
    [InlineData("descending")]
    [InlineData("ascending")]
    public void GetIndexes_PostgreSqlApplicationParentIndexes_ArePreserved(
        string facet
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = ConfigureParentIndexNode(model, true);

                if (facet == "annotation-filter")
                {
                    entity
                        .HasOne<IndexNode>()
                        .WithMany()
                        .HasForeignKey(node => new
                        {
                            node.Scope,
                            node.Parent,
                        })
                        .HasPrincipalKey(node => new
                        {
                            node.Scope,
                            node.Key,
                        })
                        .OnDelete(DeleteBehavior.Restrict);

                    ((IConventionIndex)entity.Metadata.FindIndex(
                    [
                        entity
                            .Property(node => node.Scope)
                            .Metadata,
                        entity
                            .Property(node => node.Parent)
                            .Metadata,
                    ])!).SetFilter(null, true);

                    return;
                }

                var index = facet == "named"
                    ? entity.HasIndex(
                        node => new
                        {
                            node.Scope,
                            node.Parent,
                        },
                        "ApplicationParent")
                    : entity.HasIndex(node => new
                    {
                        node.Scope,
                        node.Parent,
                    });

                switch (facet)
                {
                    case "filter":
                        index.HasFilter("\"Parent\" > 0");
                        break;
                    case "unfiltered":
                        index.HasFilter(null);
                        break;
                    case "method":
                        index.HasMethod("hash");
                        break;
                    case "database-name":
                        index.HasDatabaseName("ApplicationParentDatabaseName");
                        break;
                    case "unique":
                        index.IsUnique();
                        break;
                    case "descending":
                        index.IsDescending(true, true);
                        break;
                    case "ascending":
                        index.IsDescending(false, false);
                        break;
                }
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        var indexes = entity
            .GetIndexes()
            .ToArray();

        // Assert
        var dependent = Assert.Single(indexes, index => index.Properties[^1].Name == nameof(IndexNode.Parent));
        Assert.Equal(
            facet == "filter"
                ? "\"Parent\" > 0"
                : null,
            dependent.GetFilter());

        Assert.Equal(
            facet == "named"
                ? "ApplicationParent"
                : null,
            dependent.Name);

        Assert.Equal(facet == "unique", dependent.IsUnique);
        Assert.Equal(4, indexes.Length);
        Assert.DoesNotContain(
            indexes,
            index => index
                    .GetFilter()
                    ?.EndsWith("IS NULL", StringComparison.Ordinal)
                == true);

        if (facet == "method")
        {
            Assert.Equal("hash", dependent["Npgsql:IndexMethod"]);
        }

        if (facet == "database-name")
        {
            Assert.Equal("ApplicationParentDatabaseName", dependent.GetDatabaseName());
        }

        if (facet is "descending" or "ascending")
        {
            Assert.Equal(
                facet == "descending"
                    ? []
                    : null,
                dependent.IsDescending);
        }
    }

    /// <summary>Application structural indexes retain their facets alongside any generated tree fallback.</summary>
    /// <param name="facet">The application facet that must remain unchanged.</param>
    [Theory]
    [InlineData("unnamed")]
    [InlineData("named")]
    [InlineData("unfiltered")]
    [InlineData("filter")]
    [InlineData("unique")]
    [InlineData("descending")]
    [InlineData("method")]
    public void GetIndexes_PostgreSqlApplicationTreeIndexes_ArePreserved(
        string facet
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = ConfigureParentIndexNode(model, true);
                var index = facet == "named"
                    ? entity.HasIndex(
                        node => new
                        {
                            node.Scope,
                            node.TreeId,
                            node.Left,
                        },
                        "ApplicationTree")
                    : entity.HasIndex(node => new
                    {
                        node.Scope,
                        node.TreeId,
                        node.Left,
                    });

                index.HasDatabaseName("ApplicationTreeDatabaseName");

                switch (facet)
                {
                    case "unfiltered":
                        index.HasFilter(null);
                        break;
                    case "filter":
                        index.HasFilter("\"Left\" > 0");
                        break;
                    case "unique":
                        index.IsUnique();
                        break;
                    case "descending":
                        index.IsDescending();
                        break;
                    case "method":
                        index.HasMethod("hash");
                        break;
                }
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        var application = entity
            .GetIndexes()
            .Single(index => index.GetDatabaseName() == "ApplicationTreeDatabaseName");

        // Assert
        Assert.Equal(
            facet == "named"
                ? "ApplicationTree"
                : null,
            application.Name);

        Assert.Equal(
            facet == "filter"
                ? "\"Left\" > 0"
                : null,
            application.GetFilter());

        Assert.Equal(facet == "unique", application.IsUnique);
        Assert.Equal(
            facet == "descending"
                ? []
                : null,
            application.IsDescending);

        Assert.Equal(
            facet == "method"
                ? "hash"
                : null,
            application["Npgsql:IndexMethod"]);

        Assert.Equal(
            facet is "filter" or "unique" or "descending" or "method"
                ? 6
                : 5,
            entity
                .GetIndexes()
                .Count());
    }

    /// <summary>An application database-name collision retains its index and gives roots a distinct name.</summary>
    [Fact]
    public void RootIndexName_PostgreSqlRootIndexName_PreservesApplicationDatabaseName()
    {
        // Arrange
        using var baseline = new ConventionContext("PostgreSql", model => ConfigureParentIndexNode(model, true));
        var originalName = RootIndexName(baseline, typeof(IndexNode));
        using var context = new ConventionContext(
            "PostgreSql",
            model =>
            {
                var entity = ConfigureParentIndexNode(model, true);
                entity
                    .HasIndex(node => node.OtherKey)
                    .HasDatabaseName(originalName);
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IndexNode))!;

        var rootName = RootIndexName(context, typeof(IndexNode));

        // Assert
        var application = Assert.Single(
            entity.GetIndexes(),
            index => index.Properties is [{ Name: nameof(IndexNode.OtherKey) }]);

        Assert.Equal(originalName, application.GetDatabaseName());
        Assert.Null(application.GetFilter());
        Assert.Equal(originalName + "_1", rootName);
        Assert.Equal(
            6,
            entity
                .GetIndexes()
                .Count());
    }
}
