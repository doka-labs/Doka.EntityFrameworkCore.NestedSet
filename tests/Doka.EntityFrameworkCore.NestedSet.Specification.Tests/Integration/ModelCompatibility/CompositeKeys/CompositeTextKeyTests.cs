namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies ordinal rowset refresh for a text NodeKey that repeats across tenants.</summary>
[Collection("Model compatibility")]
public abstract class CompositeTextKeyTests : ProviderTest
{
    private const int Children = 30;

    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares one provider database with the other mapping-compatibility tests.</summary>
    protected CompositeTextKeyTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Ordered refresh keeps equal text keys in their own tenant when both tenants change.</summary>
    [Fact]
    public async Task OrderedSaveRefreshesEqualTextKeysByScopeAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<CompositeTextKeyContext>(
            Engine,
            static options => new CompositeTextKeyContext(options));

        var first = context
            .NestedSet<CompositeTextKeyNode>()
            .ForScope(21);

        var second = context
            .NestedSet<CompositeTextKeyNode>()
            .ForScope(22);

        await first.InsertRootAsync(
            new CompositeTextKeyNode
            {
                NodeKey = "root",
                Name = "Root",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await first.InsertChildAsync(
            new CompositeTextKeyNode
            {
                NodeKey = "alpha",
                Name = "A",
            },
            "root",
            CancellationToken.None);

        await first.InsertChildAsync(
            new CompositeTextKeyNode
            {
                NodeKey = "bravo",
                Name = "B",
            },
            "root",
            CancellationToken.None);

        await second.InsertRootAsync(
            new CompositeTextKeyNode
            {
                NodeKey = "root",
                Name = "Root",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await second.InsertChildAsync(
            new CompositeTextKeyNode
            {
                NodeKey = "alpha",
                Name = "A",
            },
            "root",
            CancellationToken.None);

        await second.InsertChildAsync(
            new CompositeTextKeyNode
            {
                NodeKey = "bravo",
                Name = "B",
            },
            "root",
            CancellationToken.None);

        await second.InsertChildAsync(
            new CompositeTextKeyNode
            {
                NodeKey = "charlie",
                Name = "C",
            },
            "root",
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var tracked = await context
            .Set<CompositeTextKeyNode>()
            .Where(node => node.TenantId == 21 || node.TenantId == 22)
            .ToArrayAsync(CancellationToken.None);

        // WHY: The two tenants reorder differently, so a key matched in the wrong tenant yields other bounds.
        Node(21, "alpha").Name = "Z";
        Node(22, "charlie").Name = "0";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        Assert.Equal((2L, 3L, 0L), Bounds(Node(21, "bravo")));
        Assert.Equal((4L, 5L, 1L), Bounds(Node(21, "alpha")));
        Assert.Equal((2L, 3L, 0L), Bounds(Node(22, "charlie")));
        Assert.Equal((4L, 5L, 1L), Bounds(Node(22, "alpha")));
        Assert.Equal((6L, 7L, 2L), Bounds(Node(22, "bravo")));
        return;

        CompositeTextKeyNode Node(
            int tenant,
            string key
        ) => tracked.Single(node => node.TenantId == tenant && node.NodeKey == key);
    }

    /// <summary>Tracked capture for a Parent save keeps each rowset statement within one key batch.</summary>
    [Fact]
    public async Task ParentSaveCaptureKeepsRowsetStatementsBoundedAsync()
    {
        // Arrange
        var probe = new CommandTextProbe();
        await using var context = await _fixture.CreateContextAsync<CompositeTextKeyContext>(
            Engine,
            static options => new CompositeTextKeyContext(options),
            probe);

        var trees = new Guid[3];

        for (var tree = 0; tree < trees.Length; tree++)
        {
            trees[tree] = Guid.NewGuid();
            var root = $"t{tree}-root";
            context.Add(
                new CompositeTextKeyNode
                {
                    TenantId = 23,
                    NodeKey = root,
                    TreeId = trees[tree],
                    Name = "Root",
                    Left = 1,
                    Right = 2 + (2 * Children),
                });

            for (var index = 0; index < Children; index++)
            {
                context.Add(
                    new CompositeTextKeyNode
                    {
                        TenantId = 23,
                        NodeKey = $"t{tree}-c{index:D2}",
                        TreeId = trees[tree],
                        ParentNodeKey = root,
                        Name = $"c{index:D2}",
                        Left = 2 + (2 * index),
                        Right = 3 + (2 * index),
                        Depth = 1,
                        Position = index,
                    });
            }
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var tracked = await context
            .Set<CompositeTextKeyNode>()
            .Where(node => node.TenantId == 23)
            .ToArrayAsync(CancellationToken.None);

        // WHY: Renames lock and track all three trees, and the Parent change triggers tracked-row capture
        // for more tracked keys than one rowset batch holds.
        for (var tree = 0; tree < trees.Length; tree++)
        {
            Node($"t{tree}-c00").Name = "zz";
        }

        var moved = Node("t0-c05");
        moved.ParentNodeKey = "t0-c06";
        probe.Commands.Clear();

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        var unions = probe.Commands.Max(command => command.Split("UNION ALL").Length - 1);
        Assert.InRange(unions, 0, NestedSetBatch.MaximumRows - 1);
        Assert.Equal(("t0-c06", 2), (moved.ParentNodeKey, moved.Depth));
        Assert.Equal((2L * Children, (2L * Children) + 1, Children - 1L), Bounds(Node("t1-c00")));

        foreach (var treeId in trees)
        {
            var report = await context
                .NestedSet<CompositeTextKeyNode>()
                .ForScope(23)
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

            Assert.True(report.IsValid);
        }

        return;

        CompositeTextKeyNode Node(
            string key
        ) => tracked.Single(node => node.NodeKey == key);
    }

    /// <summary>Returns the refreshed structural coordinates compared by these cases.</summary>
    private static (long Left, long Right, long Position) Bounds(
        CompositeTextKeyNode node
    ) => (node.Left, node.Right, node.Position);

    /// <summary>Captures query text without reading parameter values or retaining entities.</summary>
    private sealed class CommandTextProbe : DbCommandInterceptor
    {
        /// <summary>Gets the executed reader command templates.</summary>
        internal List<string> Commands { get; } = [];

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }
    }
}
