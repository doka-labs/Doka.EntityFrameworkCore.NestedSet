namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>Rejects a tracked sort change when an ordinary context omits the coordinated save wrapper.</summary>
    [Fact]
    public async Task OptionsGuardRejectsUncoordinatedSortChange()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(setup));
        var options = (DbContextOptions)setup.GetService<IDbContextOptions>();
        await using var context = new MissingIntegrationOrderingContext(options);
        var node = await context.Set<OrderingNode>()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        node.Name = "Zulu";

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal("Alpha", await setup.Set<OrderingNode>()
            .Where(value => value.Id == 2)
            .Select(value => value.Name)
            .SingleAsync(CancellationToken.None));
    }

    /// <summary>Allows an ordinary context to persist payload when no hierarchy coordination is required.</summary>
    [Fact]
    public async Task OptionsGuardAllowsPayloadOnlySave()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(Tree(setup));
        var options = (DbContextOptions)setup.GetService<IDbContextOptions>();
        await using var context = new MissingIntegrationOrderingContext(options);
        var node = await context.Set<OrderingNode>()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        node.Payload = "ordinary payload";

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal("ordinary payload", await setup.Set<OrderingNode>()
            .Where(value => value.Id == 2)
            .Select(value => value.Payload)
            .SingleAsync(CancellationToken.None));
    }

    /// <summary>Rejects direct insertion through DbSet before Entity Framework Core sends the row.</summary>
    [Fact]
    public async Task OptionsGuardRejectsDirectAdd()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var options = (DbContextOptions)setup.GetService<IDbContextOptions>();
        await using var context = new MissingIntegrationOrderingContext(options);
        context.Add(Node(1, "Root"));

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Empty(await setup.Set<OrderingNode>().ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Rejects direct deletion through DbSet before Entity Framework Core sends the command.</summary>
    [Fact]
    public async Task OptionsGuardRejectsDirectDelete()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await Tree(setup).InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        var options = (DbContextOptions)setup.GetService<IDbContextOptions>();
        await using var context = new MissingIntegrationOrderingContext(options);
        var node = await context.Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        context.Remove(node);

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal(1, await setup.Set<OrderingNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>Rejects direct writes to hierarchy-owned coordinates before persistence.</summary>
    [Fact]
    public async Task OptionsGuardRejectsDirectCoordinateChange()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await Tree(setup).InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        var options = (DbContextOptions)setup.GetService<IDbContextOptions>();
        await using var context = new MissingIntegrationOrderingContext(options);
        var node = await context.Set<OrderingNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        node.Left = 2;

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal(1, await setup.Set<OrderingNode>()
            .Where(value => value.Id == 1)
            .Select(value => value.Left)
            .SingleAsync(CancellationToken.None));
    }
}
