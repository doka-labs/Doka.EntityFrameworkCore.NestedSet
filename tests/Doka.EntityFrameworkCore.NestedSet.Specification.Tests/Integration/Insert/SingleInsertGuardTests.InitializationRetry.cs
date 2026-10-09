namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>An early initialization rejection leaves the same corrected input usable for retry.</summary>
    /// <param name="fault">Selects the prerequisite getter, snapshot, or foreign-navigation fault.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RejectedSingleInitializationAllowsInputRetry(
        int fault
    )
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewCustomInput(Guid.NewGuid());
        var failure = InjectSingleInitializationFault(input, fault);
        var tree = context.NestedSet<SingleIdentityNode>();
        var rejection = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            input,
            Guid.Empty,
            CancellationToken.None));

        ClearSingleInitializationFault(input);

        // Act
        await tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None);

        // Assert
        if (fault >= 2)
        {
            Assert.Equal(
                NestedSetErrorCode.InvalidContext,
                Assert.IsType<NestedSetException>(rejection)
                    .Code);
        }
        else
        {
            Assert.Same(failure, rejection);
        }

        var saved = Assert.Single(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));

        Assert.Equal(3, saved.Id.Value);
        Assert.Equal("owned", saved.Details.Label);
        Assert.Equal(
            (3, Guid.Empty, 1L, 2L, 0, 0L),
            (input.Id.Value, input.TreeId, input.Left, input.Right, input.Depth, input.Position));
        Assert.Null(input.ParentId);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Rejecting tracked input preserves its caller-owned native identity and owned payload.</summary>
    [Fact]
    public async Task TrackedSingleInputRejectionPreservesCallerIdentity()
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        var input = NewCustomInput(Guid.Empty);
        input.ParentId = null;
        input.Left = 1;
        input.Right = 2;
        input.Depth = 0;
        input.Position = 0;
        await context.AddAsync(marker, CancellationToken.None);
        await context.AddAsync(input, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<SingleIdentityNode>()
            .InsertRootAsync(input, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(EntityState.Unchanged, context.Entry(input).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(input.Details).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(
            3,
            context
                .ChangeTracker
                .Entries()
                .Count());
        Assert.Same(
            input,
            await context.FindAsync<SingleIdentityNode>([new SingleIdentityKey(3)], CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .CountAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Graph preparation cannot take ownership of payload already tracked for another caller row.</summary>
    [Fact]
    public async Task TrackedOwnedPayloadRejectionPreservesCallerAggregate()
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        var existing = NewCustomInput(Guid.Empty);
        existing.ParentId = null;
        existing.Left = 1;
        existing.Right = 2;
        existing.Depth = 0;
        existing.Position = 0;
        await context.AddAsync(marker, CancellationToken.None);
        await context.AddAsync(existing, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var input = NewCustomInput(Guid.NewGuid());
        input.Id = new SingleIdentityKey(5);
        input.Details = existing.Details;

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<SingleIdentityNode>()
            .InsertRootAsync(input, Guid.NewGuid(), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(EntityState.Unchanged, context.Entry(existing).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(existing.Details).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(
            3,
            context
                .ChangeTracker
                .Entries()
                .Count());
        Assert.Same(
            existing,
            await context.FindAsync<SingleIdentityNode>([new SingleIdentityKey(3)], CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .CountAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.Equal((5, 71L, 72L, 3, 4L), (input.Id.Value, input.Left, input.Right, input.Depth, input.Position));
        Assert.Same(existing.Details, input.Details);
    }

    /// <summary>Rejecting foreign payload preserves a relationship already owned by the caller's tracker.</summary>
    [Fact]
    public async Task TrackedForeignOwnedNavigationRejectionPreservesCallerIdentity()
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        var foreign = new SingleIdentityForeign { Id = 5 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.AddAsync(foreign, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewCustomInput(Guid.NewGuid());
        input.Details.Foreign = foreign;

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<SingleIdentityNode>()
            .InsertRootAsync(input, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(EntityState.Unchanged, context.Entry(foreign).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(marker).State);
        Assert.Equal(
            2,
            context
                .ChangeTracker
                .Entries()
                .Count());
        Assert.Same(foreign, input.Details.Foreign);
        Assert.Same(foreign, await context.FindAsync<SingleIdentityForeign>([5], CancellationToken.None));
        Assert.Empty(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<SingleIdentityForeign>()
                .AsNoTracking()
                .CountAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.Equal((3, 71L, 72L, 3, 4L), (input.Id.Value, input.Left, input.Right, input.Depth, input.Position));
    }

    /// <summary>Attaches an initialization fault without retaining EF state or changing structure.</summary>
    private static InvalidOperationException InjectSingleInitializationFault(
        SingleIdentityNode input,
        int fault
    )
    {
        var failure = new InvalidOperationException("Injected single initialization failure.");
        var throwOnce = true;
        var inject = () =>
        {
            if (throwOnce)
            {
                throwOnce = false;

                throw failure;
            }
        };

        if (fault == 0)
        {
            input.ReadKey = inject;
        }
        else if (fault == 1)
        {
            input.Id.OnSnapshot = inject;
        }
        else if (fault == 2)
        {
            input.Foreign = new SingleIdentityForeign { Id = 5 };
        }
        else
        {
            input.Details.Foreign = new SingleIdentityForeign { Id = 5 };
        }

        return failure;
    }

    /// <summary>Removes the rejected test fault before observing the same input's positive retry.</summary>
    private static void ClearSingleInitializationFault(
        SingleIdentityNode input
    )
    {
        input.ReadKey = null;
        input.Id.OnSnapshot = null;
        input.Foreign = null;
        input.Details.Foreign = null;
    }
}
