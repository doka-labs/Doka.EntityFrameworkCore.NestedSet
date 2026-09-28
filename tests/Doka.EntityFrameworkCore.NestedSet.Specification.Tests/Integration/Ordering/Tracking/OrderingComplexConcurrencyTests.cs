namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies complex generated concurrency leaves after structural SQL and subsequent payload saves.</summary>
public abstract partial class OrderingComplexConcurrencyTests : ProviderTest
{
    private readonly SaveSemanticsFixture _fixture;

    /// <summary>Uses fixture-owned engines with independently reset computed-token tables.</summary>
    protected OrderingComplexConcurrencyTests(
        IProviderFixture<SaveSemanticsFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Refreshes complex generated values while preserving the requested acceptance semantics.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameRefreshesComplexConcurrencyValues(
        bool acceptChanges
    )
    {
        // Arrange
        var database = await _fixture.PrepareAsync(Engine, PrepareAsync);
        await using var context = new ComplexConcurrencyContext(await SaveSemanticsFixture.OptionsAsync(database));
        var nodes = await context
            .Set<ComplexConcurrencyNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var previousToken = nodes[0].Details.Revision;
        nodes[0].Name = "Zulu";

        // Act
        var saved = await context.SaveChangesAsync(acceptChanges, CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);

        var token = context
            .Entry(nodes[0])
            .ComplexProperty(node => node.Details)
            .Property(value => value.Revision);

        var sibling = context
            .Entry(nodes[1])
            .ComplexProperty(node => node.Details)
            .Property(value => value.Revision);
        Assert.Equal(10, token.CurrentValue);
        Assert.Equal(acceptChanges ? 10 : previousToken, token.OriginalValue);
        Assert.Equal(5, sibling.CurrentValue);
        Assert.Equal(5, sibling.OriginalValue);
        Assert.Equal(
            acceptChanges ? EntityState.Unchanged : EntityState.Modified,
            context.Entry(nodes[0]).State);
        await using var verification = new ComplexConcurrencyContext(await SaveSemanticsFixture.OptionsAsync(database));
        Assert.Equal(
            10,
            await verification
                .Set<ComplexConcurrencyNode>()
                .Where(node => node.Id == 1)
                .Select(node => node.Details.Revision)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Proves that a later payload update does not use a stale complex concurrency token.</summary>
    [Fact]
    public async Task PayloadSaveAfterRenameUsesFinalComplexConcurrencyToken()
    {
        // Arrange
        var database = await _fixture.PrepareAsync(Engine, PrepareAsync);
        await using var context = new ComplexConcurrencyContext(await SaveSemanticsFixture.OptionsAsync(database));
        var node = await context
            .Set<ComplexConcurrencyNode>()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        node.Name = "Zulu";
        await context.SaveChangesAsync(CancellationToken.None);
        node.Payload = "later application update";

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.Null(failure);
        await using var verification = new ComplexConcurrencyContext(await SaveSemanticsFixture.OptionsAsync(database));
        Assert.Equal(
            "later application update",
            await verification
                .Set<ComplexConcurrencyNode>()
                .Where(value => value.Id == 1)
                .Select(value => value.Payload)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Returns an ordered insertion with the complex token for its final structural position.</summary>
    [Fact]
    public async Task OrderedInsertionReturnsFinalComplexConcurrencyToken()
    {
        // Arrange
        var database = await _fixture.PrepareAsync(Engine, PrepareAsync);
        await using var context = new ComplexConcurrencyContext(await SaveSemanticsFixture.OptionsAsync(database));
        var tree = context
            .NestedSet<ComplexConcurrencyNode>()
            .ForScope(1);

        var node = new ComplexConcurrencyNode
        {
            Id = 3,
            Name = "Aardvark",
        };

        // Act
        await tree.InsertChildAsync(node, 10, CancellationToken.None);

        // Assert
        Assert.Equal(EntityState.Detached, context.Entry(node).State);
        Assert.Equal(5, node.Details.Revision);
        Assert.Equal(
            node.Details.Revision,
            await tree
                .TreeContaining(3)
                .Where(value => value.Id == 3)
                .Select(value => value.Details.Revision)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Creates or clears the isolated model and arranges two ordered siblings under one surviving root.
    /// </summary>
    private static async Task PrepareAsync(
        TestDatabase database,
        bool firstUse
    )
    {
        await using var context = new ComplexConcurrencyContext(await SaveSemanticsFixture.OptionsAsync(database));
        if (firstUse)
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);
        }
        else
        {
            await context
                .Set<ComplexConcurrencyNode>()
                .ExecuteDeleteAsync(CancellationToken.None);
        }

        var tree = context
            .NestedSet<ComplexConcurrencyNode>()
            .ForScope(1);

        await tree.InsertRootAsync(
            new ComplexConcurrencyNode
            {
                Id = 10,
                Name = "Root",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        await tree.InsertChildAsync(
            new ComplexConcurrencyNode
            {
                Id = 1,
                Name = "Alpha",
            },
            10,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new ComplexConcurrencyNode
            {
                Id = 2,
                Name = "Bravo",
            },
            10,
            CancellationToken.None);
    }
}
