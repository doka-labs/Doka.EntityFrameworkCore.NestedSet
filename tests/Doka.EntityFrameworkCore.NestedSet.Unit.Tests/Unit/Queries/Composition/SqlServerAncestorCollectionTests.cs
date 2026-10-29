namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects real owned and ordinary collection identifiers in SQL Server ancestor projections.</summary>
public sealed class SqlServerAncestorCollectionTests
{
    /// <summary>Entity auto-includes and collection DTOs translate on both real navigation models.</summary>
    /// <param name="indexed">Whether the common model includes the optional ancestor index.</param>
    /// <param name="includeSelf">Whether self occurrences use the existing inclusive path.</param>
    /// <param name="dto">Whether collections are projected rather than materialized on entities.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task RealNavigationModelsTranslateCollectionsWithoutIo(bool indexed, bool includeSelf, bool dto)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = indexed
            ? (DbContextOptions)ModelCompatibilityDatabase.Options<AncestorIndexQueryContext>("SqlServer")
            : ModelCompatibilityDatabase.Options<CompositionNavigationContext>("SqlServer");

        var configured = indexed
            ? (DbContextOptions)new DbContextOptionsBuilder<AncestorIndexQueryContext>(
                (DbContextOptions<AncestorIndexQueryContext>)options).AddInterceptors(boundary).Options
            : new DbContextOptionsBuilder<CompositionNavigationContext>(
                (DbContextOptions<CompositionNavigationContext>)options).AddInterceptors(boundary).Options;

        await using var services = NestedSetTestAdapters.BuildServices(configured);
        await using var context = CreateContext(configured, services, indexed);
        var tree = context.NestedSet<CompositionNavigationNode>().ForScope(Guid.Empty).InTree(Guid.Empty);
        var pairs = includeSelf
            ? tree.SelectAncestorsOrSelf(node => node.Id >= 2,
                (source, owner, distance) => new NavigationPair { Source = source, Owner = owner, Distance = distance })
            : tree.SelectAncestors(node => node.Id >= 2,
                (source, owner, distance) => new NavigationPair { Source = source, Owner = owner, Distance = distance });

        // Act
        var sql = dto ? CollectionDtos(pairs).AsSingleQuery().ToQueryString() : pairs.AsSingleQuery().ToQueryString();

        // Assert
        Assert.Contains("LEFT JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("Tags", sql, StringComparison.Ordinal);
        Assert.Equal(dto, sql.Contains("Items", StringComparison.Ordinal));
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Owns each real model's provider graph without process-global service-provider caching.</summary>
    private static DbContext CreateContext(DbContextOptions options, ServiceProvider services, bool indexed)
        => indexed
            ? new AncestorIndexQueryContext(new DbContextOptionsBuilder<AncestorIndexQueryContext>(
                    (DbContextOptions<AncestorIndexQueryContext>)options)
                .UseInternalServiceProvider(services).Options)
            : new CompositionNavigationContext(new DbContextOptionsBuilder<CompositionNavigationContext>(
                    (DbContextOptions<CompositionNavigationContext>)options)
                .UseInternalServiceProvider(services).Options);

    /// <summary>Projects both actual aliases without materializing mapped principals or their dependents.</summary>
    private static IQueryable<NavigationDto> CollectionDtos(IQueryable<NavigationPair> pairs)
        => pairs.Select(row => new NavigationDto
        {
            Source = row.Source.Id, Owner = row.Owner.Id, Distance = row.Distance,
            SourceCaption = row.Source.Details.Caption, OwnerCaption = row.Owner.Details.Caption,
            SourceTags = row.Source.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
            OwnerTags = row.Owner.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
            SourceItems = row.Source.Items.OrderBy(item => item.Id).Select(item => item.Label).ToArray(),
            OwnerItems = row.Owner.Items.OrderBy(item => item.Id).Select(item => item.Label).ToArray(),
        });

    /// <summary>Retains the actual mapped aliases so EF must preserve collection identifiers.</summary>
    private sealed class NavigationPair
    {
        /// <summary>Gets the selected mapped source.</summary>
        public required CompositionNavigationNode Source { get; init; }
        /// <summary>Gets its actual mapped ancestor.</summary>
        public required CompositionNavigationNode Owner { get; init; }
        /// <summary>Gets their structural distance.</summary>
        public int Distance { get; init; }
    }

    /// <summary>Retains each original pair and all projected collection occurrence values.</summary>
    private sealed class NavigationDto
    {
        /// <summary>Gets the source identity.</summary>
        public int Source { get; init; }
        /// <summary>Gets the ancestor identity.</summary>
        public int Owner { get; init; }
        /// <summary>Gets the structural distance.</summary>
        public int Distance { get; init; }
        /// <summary>Gets the source's owned reference value.</summary>
        public string SourceCaption { get; init; } = string.Empty;
        /// <summary>Gets the ancestor's owned reference value.</summary>
        public string OwnerCaption { get; init; } = string.Empty;
        /// <summary>Gets the source's owned collection.</summary>
        public string[] SourceTags { get; init; } = [];
        /// <summary>Gets the ancestor's owned collection.</summary>
        public string[] OwnerTags { get; init; } = [];
        /// <summary>Gets the source's ordinary collection.</summary>
        public string[] SourceItems { get; init; } = [];
        /// <summary>Gets the ancestor's ordinary collection.</summary>
        public string[] OwnerItems { get; init; } = [];
    }
}
