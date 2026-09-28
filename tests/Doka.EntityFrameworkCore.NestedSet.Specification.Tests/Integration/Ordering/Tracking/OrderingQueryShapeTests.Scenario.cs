namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingQueryShapeTests
{
    /// <summary>Uses the same TreeId in two Scopes to require the complete identity throughout refresh.</summary>
    private static readonly Guid s_shapeTreeId = Guid.Parse("b9614914-82ad-42ec-b6f6-6d9d4b2b5dcc");

    /// <summary>Seeds two overlapping coordinate spaces without exercising automatic ordering during setup.</summary>
    private static async Task SeedAsync(
        OrderingKeyContext setup
    )
    {
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
            .ConfigureTestWarnings()
            .UseModel(setup.Model)
            .Options;

        // WHY: The seed already satisfies nested-set ordering; a plain context avoids measuring setup mutations.
        await using var seed = new DbContext(options);
        var rows = Enumerable
            .Range(1, 2)
            .SelectMany(scope => Enumerable
                .Range(0, 192)
                .Select(position => CreateNode(scope, position, false))
                .Prepend(CreateRoot(scope)))
            .ToArray();

        await seed.AddRangeAsync(rows, CancellationToken.None);
        await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Preserves provider options and model, with regular observers and optional test-owned cache services.
    /// </summary>
    private static OrderingKeyContext CreateContext(
        OrderingKeyContext setup,
        ScaleProbe probe,
        OrdinalProbe ordinals,
        IServiceProvider? services = null
    )
    {
        var extensions = setup
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder<OrderingKeyContext>(
                new DbContextOptions<OrderingKeyContext>(extensions))
            .ConfigureTestWarnings()
            .UseModel(setup.Model)
            .AddInterceptors(probe, ordinals);

        if (services is not null)
        {
            options.UseInternalServiceProvider(services);
        }

        return new OrderingKeyContext(options.Options);
    }

    /// <summary>
    ///     Tracks a full first-member batch and a second-member tail using database-equal lowercase aliases.
    /// </summary>
    private static OrderingKeyNode<string, string?>[] AttachScenario(
        OrderingKeyContext context,
        int scope,
        int intervals,
        int tail
    )
    {
        var positions = Enumerable
            .Range(0, 64)
            .Select(triple => triple * 3)
            .Concat(
                Enumerable
                    .Range(0, tail)
                    .Select(triple => (triple * 3) + 1));

        var tracked = positions
            .Select(position => CreateNode(scope, position, true))
            .ToArray();

        context.AttachRange(tracked);

        for (var index = 0; index < intervals; index++)
        {
            // WHY: The third sibling stays fixed, separating adjacent swaps into disjoint write intervals.
            tracked[index].Name = $"N{index:D3}-bz";
        }

        return tracked;
    }

    /// <summary>Constructs one canonical seed row or an equivalent attached native-key alias.</summary>
    private static OrderingKeyNode<string, string?> CreateNode(
        int scope,
        long position,
        bool alias
    )
    {
        var key = $"S{scope}-N{position:D3}";
        var member = (char)('a' + (position % 3));

        return new OrderingKeyNode<string, string?>(
            alias ? key.ToLowerInvariant() : key,
            $"N{position / 3:D3}-{member}")
        {
            Scope = scope,
            TreeId = s_shapeTreeId,
            ParentId = $"S{scope}-ROOT",
            Left = (position * 2) + 2,
            Right = (position * 2) + 3,
            Depth = 1,
            Position = position,
        };
    }

    /// <summary>Provides one surviving root outside the measured child tracker and refresh batches.</summary>
    private static OrderingKeyNode<string, string?> CreateRoot(
        int scope
    ) => new($"S{scope}-ROOT", "Root")
    {
        Scope = scope,
        TreeId = s_shapeTreeId,
        Left = 1,
        Right = 386,
    };

    /// <summary>Captures only events after the last actual coordinate update in a completed production save.</summary>
    private static ShapeObservation Capture(
        ScaleProbe probe,
        OrdinalProbe ordinals
    )
    {
        var lastWrite = probe.Commands.FindLastIndex(command => IsStructuralUpdate(command.Sql));
        var commands = probe
            .Commands
            .Skip(lastWrite + 1)
            .ToArray();

        var readers = ordinals
            .Readers
            .TakeLast(commands.Length)
            .ToArray();

        var lastWriteSql = lastWrite >= 0 ? probe.Commands[lastWrite].Sql : string.Empty;

        return new ShapeObservation(
            lastWrite,
            lastWriteSql,
            probe.Compilations,
            probe.CompilationCommandOffsets.Count(offset => offset > lastWrite),
            commands,
            readers);
    }

    /// <summary>
    ///     Recognizes coordinate assignments, including a move's final standalone sibling-position update.
    /// </summary>
    private static bool IsStructuralUpdate(
        string sql
    )
    {
        if (!sql
                .TrimStart()
                .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var set = sql.IndexOf("SET ", StringComparison.OrdinalIgnoreCase);
        var where = sql.IndexOf("WHERE ", StringComparison.OrdinalIgnoreCase);
        if (set < 0
            || where <= set)
        {
            return false;
        }

        // WHY: Single-node moves update Position after bounds; only checking Left mislabels that write as refresh.
        // Inspect assignments rather than WHERE predicates so an ordinary payload write cannot match by accident.
        var assignments = sql[set..where];

        return assignments.Contains(nameof(OrderingKeyNode<,>.Left), StringComparison.Ordinal)
            || assignments.Contains(nameof(OrderingKeyNode<,>.Right), StringComparison.Ordinal)
            || assignments.Contains(nameof(OrderingKeyNode<,>.Depth), StringComparison.Ordinal)
            || assignments.Contains(nameof(OrderingKeyNode<,>.Position), StringComparison.Ordinal)
            || assignments.Contains(nameof(OrderingKeyNode<,>.ParentId), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies actual projected ordinals, including every affected full-batch and tail entry exactly once.
    /// </summary>
    private static void AssertOrdinals(
        ShapeObservation observation,
        int intervals,
        int tail
    )
    {
        Assert.Equal(3, observation.Readers.Length);
        Assert.Equal(
            observation.Commands.Select(command => command.Sql),
            observation.Readers.Select(reader => reader.Sql));

        Assert.Equal(Enumerable.Range(0, 1), observation.Readers[0].Ordinals);
        Assert.Equal(
            Enumerable.Range(0, intervals),
            observation
                .Readers[1]
                .Ordinals
                .OrderBy(value => value));
        Assert.Equal(
            Enumerable.Range(0, Math.Min(intervals, tail)),
            observation
                .Readers[2]
                .Ordinals
                .OrderBy(value => value));
    }

    /// <summary>
    ///     Checks every attached entry's refreshed geometry and preserves the original pending ordering value.
    /// </summary>
    private static void AssertTracked(
        OrderingKeyContext context,
        OrderingKeyNode<string, string?>[] tracked,
        int intervals,
        int tail
    )
    {
        Assert.Equal(64 + tail, tracked.Length);
        for (var index = 0; index < tracked.Length; index++)
        {
            var first = index < 64;
            var triple = first ? index : index - 64;
            var initial = (triple * 3) + (first ? 0 : 1);
            var expected = (long)(triple < intervals ? initial + (first ? 1 : -1) : initial);
            var node = tracked[index];
            Assert.Equal(
                ((expected * 2) + 2, (expected * 2) + 3, 1, expected),
                (node.Left, node.Right, node.Depth, node.Position));

            Assert.Equal($"S{node.Scope}-ROOT", node.ParentId);
            Assert.Equal(s_shapeTreeId, node.TreeId);
            var name = context
                .Entry(node)
                .Property(value => value.Name);
            Assert.Equal($"N{triple:D3}-{(first ? 'a' : 'b')}", name.OriginalValue);
            Assert.Equal(first && triple < intervals, name.IsModified);
            Assert.Equal(first && triple < intervals ? $"N{triple:D3}-bz" : name.OriginalValue, name.CurrentValue);
        }
    }

    /// <summary>
    ///     Reads canonical rows through an unobserved context for independent scope and geometry verification.
    /// </summary>
    private static Task<OrderingKeyNode<string, string?>[]> ReadScopeAsync(
        OrderingKeyContext context,
        int scope
    ) => context
        .Set<OrderingKeyNode<string, string?>>()
        .AsNoTracking()
        .Where(node => node.Scope == scope && node.ParentId != null)
        .OrderBy(node => node.Id)
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Checks every persisted sibling, including stable islands that were never tracked.</summary>
    private static void AssertPersisted(
        OrderingKeyNode<string, string?>[] rows,
        int intervals
    )
    {
        Assert.Equal(192, rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            var member = index % 3;
            var expected = (long)(index / 3 < intervals && member != 2
                ? index + (member == 0 ? 1 : -1)
                : index);

            Assert.Equal(
                ((expected * 2) + 2, (expected * 2) + 3, 1, expected),
                (rows[index].Left, rows[index].Right, rows[index].Depth, rows[index].Position));

            Assert.Equal($"S{rows[index].Scope}-ROOT", rows[index].ParentId);
            Assert.Equal(s_shapeTreeId, rows[index].TreeId);
        }
    }

    /// <summary>Checks that a disjoint child permutation preserves its untracked enclosing root.</summary>
    private static async Task AssertRootAsync(
        OrderingKeyContext context,
        int scope
    )
    {
        var root = await context
            .Set<OrderingKeyNode<string, string?>>()
            .AsNoTracking()
            .SingleAsync(node => node.Scope == scope && node.ParentId == null, CancellationToken.None);

        Assert.Equal($"S{scope}-ROOT", root.Id);
        Assert.Equal(s_shapeTreeId, root.TreeId);
        Assert.Equal((1, 386, 0, 0), (root.Left, root.Right, root.Depth, root.Position));
    }

    /// <summary>
    ///     Counts independently observed changed spans rather than inferring interval count from input size.
    /// </summary>
    private static int CountChangedIntervals(
        OrderingKeyNode<string, string?>[] rows
    )
    {
        var changed = rows
            .Where((node, index) => node.Position != index)
            .Select(node => node.Position)
            .OrderBy(position => position)
            .ToArray();

        var intervals = 0;
        long previous = -2;
        foreach (var position in changed)
        {
            if (position != previous + 1)
            {
                intervals++;
            }

            previous = position;
        }

        return intervals;
    }

    /// <summary>
    ///     Includes payload and complete tree identity when proving that another partition stayed unchanged.
    /// </summary>
    private static string Coordinates(
        OrderingKeyNode<string, string?> node
    ) => $"{node.Id}:{node.Scope}:{node.TreeId}:{node.Name}:{node.Left}:{node.Right}"
        + $":{node.Depth}:{node.Position}:{node.ParentId}";

    /// <summary>Retains actual completed command and compilation observations for one production save.</summary>
    private sealed record ShapeObservation(
        int LastStructuralWrite,
        string LastStructuralSql,
        int SaveCompilations,
        int RefreshCompilations,
        ScaleCommand[] Commands,
        OrdinalRead[] Readers
    );
}
