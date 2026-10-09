namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>A callback-corrupted mutable hash key produces an explicit error requiring context disposal.</summary>
    [Fact]
    public async Task RepeatedSingleIdentityMutationRequiresDiscardingTheContext()
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var treeId = Guid.NewGuid();
        var input = NewCustomInput(treeId);
        var details = input.Details;
        context.SavingChanges += (_, _) =>
        {
            input.Id.Value = 4;
            context.ChangeTracker.DetectChanges();
            input.Id.Value = 5;
        };

        // Act
        var rejection = await Record.ExceptionAsync(() => context
            .NestedSet<SingleIdentityNode>()
            .InsertRootAsync(input, Guid.Empty, CancellationToken.None));

        // Assert
        var errors = Assert
            .IsType<AggregateException>(rejection)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(errors, error => error is NestedSetException { Code: NestedSetErrorCode.InvalidStructure });
        Assert.Contains(
            errors,
            error => error is InvalidOperationException
                && error.Message.Contains("Discard the context", StringComparison.Ordinal));
        Assert.Equal(
            (3, treeId, 8, 71L, 72L, 3, 4L),
            (input.Id.Value, input.TreeId, input.ParentId?.Value, input.Left, input.Right, input.Depth,
                input.Position));
        Assert.Same(details, input.Details);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Empty(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Comparer-equal identity replacements remain valid before and after callback-owned detection.</summary>
    [Fact]
    public async Task EquivalentSingleIdentityReplacementsRemainValidAroundExplicitDetection()
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
        var details = input.Details;
        context.SavingChanges += (_, _) =>
        {
            input.Id = new SingleIdentityKey(3);
            context.ChangeTracker.DetectChanges();
            input.Id = new SingleIdentityKey(3);
        };

        // Act
        await context
            .NestedSet<SingleIdentityNode>()
            .InsertRootAsync(input, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(3, input.Id.Value);
        Assert.Same(details, input.Details);
        Assert.Equal((42, 43), (input.GeneratedValue, details.GeneratedValue));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);

        var saved = Assert.Single(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(3, saved.Id.Value);
        Assert.Equal("owned", saved.Details.Label);
        Assert.NotSame(
            input,
            await context.FindAsync<SingleIdentityNode>([new SingleIdentityKey(3)], CancellationToken.None));
    }
}
