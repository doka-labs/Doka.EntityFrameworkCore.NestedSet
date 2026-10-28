using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects real owned and ordinary collection identifiers in SQL Server ancestor projections.</summary>
public sealed partial class SqlServerAncestorCollectionTests
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

    /// <summary>The exact independent-coordinate helper retains owned payload and original collection bags.</summary>
    /// <param name="indexed">Whether the real common indexed navigation model is used.</param>
    /// <param name="dto">Whether collection values are projected into scalar DTOs.</param>
    /// <param name="selection">Visible sources, a hidden leaf, or a missing requested source.</param>
    [Theory]
    [InlineData(false, false, "visible")]
    [InlineData(false, true, "visible")]
    [InlineData(true, false, "visible")]
    [InlineData(true, true, "visible")]
    [InlineData(false, false, "hidden")]
    [InlineData(false, true, "hidden")]
    [InlineData(true, false, "hidden")]
    [InlineData(true, true, "hidden")]
    [InlineData(false, false, "missing")]
    [InlineData(false, true, "missing")]
    [InlineData(true, false, "missing")]
    [InlineData(true, true, "missing")]
    public async Task ExactHelperExecutesBothCollectionShapes(bool indexed, bool dto, string selection)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var probe = new CompositionCommandProbe();
        var options = indexed
            ? (DbContextOptions)new DbContextOptionsBuilder<AncestorIndexQueryContext>()
                .UseSqlite(connection, provider => provider.UseNestedSets()).Options
            : new DbContextOptionsBuilder<CompositionNavigationContext>()
                .UseSqlite(connection, provider => provider.UseNestedSets()).Options;

        await using var services = NestedSetTestAdapters.BuildServices(options);

        // WHY: EF SQLite's async schema factory performs a synchronous metadata scalar internally.
        // The strict consumer-work observer starts only after that untimed schema boundary.
        await using (var schema = CreateContext(options, services, indexed))
        {
            await schema.Database.EnsureCreatedAsync(CancellationToken.None);
        }

        var observed = indexed
            ? (DbContextOptions)new DbContextOptionsBuilder<AncestorIndexQueryContext>(
                    (DbContextOptions<AncestorIndexQueryContext>)options)
                .AddInterceptors(probe).Options
            : new DbContextOptionsBuilder<CompositionNavigationContext>(
                    (DbContextOptions<CompositionNavigationContext>)options)
                .AddInterceptors(probe).Options;

        await using var context = CreateContext(observed, services, indexed);
        var nodes = Enumerable.Range(1, 3).Select(id => Node(id, selection)).ToArray();
        await context.AddRangeAsync(nodes, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var root = new NestedSetQuery<CompositionNavigationNode>(context,
            context.Model.FindEntityType(typeof(CompositionNavigationNode))!).ForScope(Guid.Empty);

        var minimum = selection == "missing" ? 99 : 2;
        Expression<Func<CompositionNavigationNode, bool>> selected = node => node.Id >= minimum;
        Expression<Func<CompositionNavigationNode, CompositionNavigationNode, int, NavigationPair>> projection =
            (source, owner, distance) => new NavigationPair { Source = source, Owner = owner, Distance = distance };

        // WHY: SQLite executes the exact core helper without advertising SQL Server capabilities.
        // Real SQL Server ToQueryString cases above separately qualify the selected adapter boundary.
        var pairs = (IQueryable<NavigationPair>)root.GetType().GetMethod("SelectCoordinateAncestorPairs",
                BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(typeof(NavigationPair))
            .Invoke(root, [selected, projection])!;

        var expected = selection switch
        {
            "visible" => new[] { (2, 1, 1), (3, 1, 2), (3, 2, 1) },
            "hidden" => [(2, 1, 1)],
            _ => [],
        };

        // Act
        var actual = dto
            ? await CollectionDtos(pairs).AsSingleQuery().OrderBy(row => row.Source).ThenBy(row => row.Owner)
                .ToArrayAsync(CancellationToken.None)
            : (await pairs.AsSingleQuery().OrderBy(row => row.Source.Id).ThenBy(row => row.Owner.Id)
                .ToArrayAsync(CancellationToken.None)).Select(EntityDto).ToArray();

        // Assert
        Assert.Equal(expected, actual.Select(row => (row.Source, row.Owner, row.Distance)));
        Assert.All(actual, row =>
        {
            Assert.Equal($"details-{row.Source}", row.SourceCaption);
            Assert.Equal($"details-{row.Owner}", row.OwnerCaption);
            Assert.Equal(new[] { $"tag-{row.Source}-1", $"tag-{row.Source}-2" }, row.SourceTags);
            Assert.Equal(new[] { $"tag-{row.Owner}-1", $"tag-{row.Owner}-2" }, row.OwnerTags);

            if (dto)
            {
                Assert.Equal(new[] { $"item-{row.Source}-1", $"item-{row.Source}-2" }, row.SourceItems);
                Assert.Equal(new[] { $"item-{row.Owner}-1", $"item-{row.Owner}-2" }, row.OwnerItems);
            }
        });

        Assert.Equal(1, probe.CommandCount);
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

    /// <summary>Seeds independent synthetic payload with two owned and ordinary rows per actual principal.</summary>
    private static CompositionNavigationNode Node(int id, string selection)
    {
        var node = new CompositionNavigationNode
        {
            Id = id, Scope = Guid.Empty, TreeId = Guid.Empty, ParentId = id == 1 ? null : id - 1,
            Left = id, Right = 7 - id, Depth = id - 1, Visible = selection != "hidden" || id != 3,
            Details = new CompositionNavigationDetail { Caption = $"details-{id}" },
        };

        foreach (var item in new[] { 1, 2 })
        {
            node.Tags.Add(new CompositionNavigationTag { Id = item, Value = $"tag-{id}-{item}" });
            node.Items.Add(new CompositionNavigationItem
            {
                Scope = Guid.Empty, Id = id * 10 + item, NodeId = id, Label = $"item-{id}-{item}",
            });
        }

        return node;
    }

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

    /// <summary>Compares auto-included entity payload after the single database read has completed.</summary>
    private static NavigationDto EntityDto(NavigationPair row) => new()
    {
        Source = row.Source.Id, Owner = row.Owner.Id, Distance = row.Distance,
        SourceCaption = row.Source.Details.Caption, OwnerCaption = row.Owner.Details.Caption,
        SourceTags = row.Source.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
        OwnerTags = row.Owner.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
    };

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
