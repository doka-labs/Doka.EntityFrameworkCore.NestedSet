namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     An ordinary composite relationship can change without changing its overlapping hierarchy scope.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, "add")]
    [InlineData(false, false, true, "add")]
    [InlineData(false, true, false, "add")]
    [InlineData(false, true, true, "add")]
    [InlineData(true, false, false, "add")]
    [InlineData(true, false, true, "add")]
    [InlineData(true, true, false, "add")]
    [InlineData(true, true, true, "add")]
    [InlineData(false, false, false, "move")]
    [InlineData(false, false, true, "move")]
    [InlineData(false, true, false, "move")]
    [InlineData(false, true, true, "move")]
    [InlineData(true, false, false, "move")]
    [InlineData(true, false, true, "move")]
    [InlineData(true, true, false, "move")]
    [InlineData(true, true, true, "move")]
    [InlineData(false, false, false, "remove")]
    [InlineData(false, false, true, "remove")]
    [InlineData(false, true, false, "remove")]
    [InlineData(false, true, true, "remove")]
    [InlineData(true, false, false, "remove")]
    [InlineData(true, false, true, "remove")]
    [InlineData(true, true, false, "remove")]
    [InlineData(true, true, true, "remove")]
    [InlineData(false, false, false, "clear")]
    [InlineData(false, false, true, "clear")]
    [InlineData(false, true, false, "clear")]
    [InlineData(false, true, true, "clear")]
    [InlineData(true, false, false, "clear")]
    [InlineData(true, false, true, "clear")]
    [InlineData(true, true, false, "clear")]
    [InlineData(true, true, true, "clear")]
    [InlineData(false, false, false, "reference")]
    [InlineData(false, false, true, "reference")]
    [InlineData(false, true, false, "reference")]
    [InlineData(false, true, true, "reference")]
    [InlineData(true, false, false, "reference")]
    [InlineData(true, false, true, "reference")]
    [InlineData(true, true, false, "reference")]
    [InlineData(true, true, true, "reference")]
    public async Task OverlappingPayloadRelationshipRetainsOrdinaryDetection(
        bool wrapped,
        bool automaticDetection,
        bool synchronous,
        string operation
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var assigned = operation != "add";
        await SeedPayloadOwnersAsync(database, assigned, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped,
            cancellationToken: TestContext.Current.CancellationToken);

        var node = await context
            .Set<NavigationNode>()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        var owners = await context
            .Set<NavigationPayloadOwner>()
            .Include(value => value.Nodes)
            .Where(value => value.Scope == node.Scope)
            .ToArrayAsync(CancellationToken.None);

        var previous = Assert.Single(owners, value => value.Id == 1);
        var next = Assert.Single(owners, value => value.Id == 2);
        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(CancellationToken.None);

        var before = (node.Scope, node.ParentId, node.TreeId, node.Left, node.Right, node.Depth, node.Position);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        ChangePayloadRelationship(node, previous, next, operation);
        note.Value = "ordinary change";
        await context.AddAsync(new NavigationNote { Id = 2 }, CancellationToken.None);

        // Act
        await SaveNavigationAsync(context, synchronous, cancellationToken: TestContext.Current.CancellationToken);
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var stored = await verification
            .Set<NavigationNode>()
            .Where(value => value.Id == 2)
            .Select(value => new
            {
                value.Scope,
                value.ParentId,
                value.TreeId,
                value.Left,
                value.Right,
                value.Depth,
                value.Position,
                value.PayloadOwnerId,
            })
            .SingleAsync(CancellationToken.None);

        var storedNotes = await verification
            .Set<NavigationNote>()
            .OrderBy(value => value.Id)
            .Select(value => value.Value)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        int? expectedOwner = automaticDetection
            ? operation is "remove" or "clear"
                ? null
                : 2
            : assigned
                ? 1
                : null;

        Assert.Equal(expectedOwner, stored.PayloadOwnerId);
        Assert.Equal(
            before,
            (stored.Scope, stored.ParentId, stored.TreeId, stored.Left, stored.Right,
                stored.Depth, stored.Position));

        Assert.Equal(
            [
                automaticDetection
                    ? "ordinary change"
                    : "original",
                "original",
            ],
            storedNotes);

        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A different-scope ordinary principal still cannot change the overlapping hierarchy scope.</summary>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public async Task OverlappingPayloadRelationshipCannotChangeScope(
        bool wrapped,
        bool automaticDetection,
        bool synchronous,
        bool collection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPayloadOwnersAsync(
            database,
            assigned: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var node = await context
            .Set<NavigationNode>()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        var next = await context
            .Set<NavigationPayloadOwner>()
            .Include(value => value.Nodes)
            .SingleAsync(value => value.Scope != node.Scope, CancellationToken.None);

        var scope = node.Scope;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (collection)
        {
            next.Nodes.Add(node);
        }
        else
        {
            node.PayloadOwner = next;
        }

        await context.AddAsync(new NavigationNote { Id = 2 }, CancellationToken.None);

        // Act
        var failure = await Record.ExceptionAsync(() => SaveNavigationAsync(
            context,
            synchronous,
            cancellationToken: TestContext.Current.CancellationToken));

        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var stored = await verification
            .Set<NavigationNode>()
            .Where(value => value.Id == 2)
            .Select(value => new
            {
                value.Scope,
                value.PayloadOwnerId,
                value.ParentId,
            })
            .SingleAsync(CancellationToken.None);

        var storedNotes = await verification
            .Set<NavigationNote>()
            .CountAsync(CancellationToken.None);

        // Assert
        AssertScopeChangeRejected(failure, context, wrapped, synchronous, automaticDetection);
        Assert.Equal(0, probe.Writes);
        Assert.Equal(scope, stored.Scope);
        Assert.Equal(1, stored.PayloadOwnerId);
        Assert.Equal(1, stored.ParentId);
        Assert.Equal(1, storedNotes);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Creates ordinary principals; only these overlap tests populate the optional payload relation.</summary>
    private static async Task SeedPayloadOwnersAsync(
        TestDatabase database,
        bool assigned,
        CancellationToken cancellationToken
    )
    {
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: cancellationToken);

        var scope = await context
            .Set<NavigationNode>()
            .Where(value => value.Id == 2)
            .Select(value => value.Scope)
            .SingleAsync(cancellationToken);

        var otherScope = await context
            .Set<NavigationScopeOwner>()
            .Where(value => value.Id != scope)
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);

        await context.AddRangeAsync(
            [
                new NavigationPayloadOwner
                {
                    Scope = scope,
                    Id = 1,
                },
                new NavigationPayloadOwner
                {
                    Scope = scope,
                    Id = 2,
                },
                new NavigationPayloadOwner
                {
                    Scope = otherScope,
                    Id = 2,
                },
            ],
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        if (assigned)
        {
            // WHY: Seed the ordinary FK directly so relationship-change behavior is exercised only in Act.
            await context
                .Set<NavigationNode>()
                .Where(value => value.Id == 2)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.PayloadOwnerId, 1),
                    cancellationToken);
        }
    }

    /// <summary>Expresses one ordinary navigation edit without invoking EF change detection.</summary>
    private static void ChangePayloadRelationship(
        NavigationNode node,
        NavigationPayloadOwner previous,
        NavigationPayloadOwner next,
        string operation
    )
    {
        switch (operation)
        {
            case "add":
            case "move":
                next.Nodes.Add(node);
                break;
            case "remove":
                previous.Nodes.Remove(node);
                break;
            case "clear":
                node.PayloadOwner = null;
                break;
            case "reference":
                node.PayloadOwner = next;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
