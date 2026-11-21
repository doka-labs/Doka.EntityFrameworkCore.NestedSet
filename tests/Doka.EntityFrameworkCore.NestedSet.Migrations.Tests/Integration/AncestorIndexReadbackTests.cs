namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Verifies native generated-result attribution through public assigned-key hierarchy imports.</summary>
[DatabasePlatform]
[Collection(AncestorIndexFixtureRegistration.Name)]
public sealed partial class AncestorIndexReadbackTests
{
    private const int ImportCount = 66;
    private readonly MigrationFixture _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Shares the actual provider engines and records the selected native readback shape.</summary>
    /// <param name="fixture">The owner of isolated migration databases.</param>
    /// <param name="output">The per-case qualification output.</param>
    public AncestorIndexReadbackTests(
        MigrationFixture fixture,
        ITestOutputHelper output
    )
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>Correlates each generated value before the public import's final scalar refresh.</summary>
    /// <param name="engine">The actual Doka server family.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task InsertSubtreeAsync_AssignedSiblingImport_PreservesNativeGeneratedAttribution(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null,
            qualifySchema: true);

        var observation = new ReadbackObservation();
        await using var context = CreateContext(database, engine, observation);
        await InitializeAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var nodes = CreateInputs();
        var branch = CreateBranch(nodes);
        observation.Enabled = true;

        // Act
        await context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InsertSubtreeAsync(branch, 1, CancellationToken.None);

        observation.Enabled = false;
        var persisted = await ReadRowsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var validation = await context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InTree(11)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        AssertReadback(observation, nodes, 0);
        Assert.Equal(ImportCount + 1, persisted.Length);
        Assert.All(nodes, node => Assert.Equal(node.Input * 17L + 3L, node.Stamp));
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.IsBranch));
        Assert.True(validation.IsValid);
        Assert.Empty(context.ChangeTracker.Entries());
        AssertNativeShape(engine, observation, declaredTrigger: false);
    }

    /// <summary>Uses the declared-trigger fallback and reads the real trigger-modified generated outputs.</summary>
    [Fact]
    public async Task InsertSubtreeAsync_DeclaredTriggerImport_KeepsSafeReadbackFallback()
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(
            "MySql",
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null,
            qualifySchema: true);

        var observation = new ReadbackObservation();
        await using var context = CreateContext(database, "MySql", observation, declaredTrigger: true);
        await InitializeAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var nodes = CreateInputs();
        var branch = CreateBranch(nodes);
        observation.Enabled = true;

        // Act
        await context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InsertSubtreeAsync(branch, 1, CancellationToken.None);

        observation.Enabled = false;
        var persisted = await ReadRowsAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertReadback(observation, nodes, 100);
        Assert.Equal(ImportCount + 1, persisted.Length);

        // WHY: Input remains application-owned; the import refreshes generated values rather than replacing
        // arbitrary payload with trigger changes. Validate both sides independently instead of inferring one.
        Assert.Equal(Enumerable.Range(1, ImportCount), nodes.Select(node => node.Input));
        Assert.All(
            nodes,
            node =>
            {
                Assert.Equal((node.Input + 100) * 17L + 3L, node.Stamp);
                var stored = Assert.Single(persisted, row => row.Id == node.Id);
                Assert.Equal(node.Input + 100, stored.Input);
                Assert.Equal(node.Stamp, stored.Stamp);
            });

        Assert.All(persisted, row => Assert.Equal(row.Input * 17L + 3L, row.Stamp));
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.IsBranch));
        Assert.Empty(context.ChangeTracker.Entries());
        AssertNativeShape("MySql", observation, declaredTrigger: true);
    }

    /// <summary>Restores the caller's transaction and all input values after a later completed payload save.</summary>
    /// <param name="engine">The actual Doka server family.</param>
    /// <param name="cancel">Whether the later save is interrupted by cancellation rather than an exception.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task InsertSubtreeAsync_LaterReadbackFailure_RestoresCallerTransactionAndInputs(
        string engine,
        bool cancel
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await using var database = await _fixture.CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null,
            qualifySchema: true);

        var observation = new ReadbackObservation
        {
            FailLaterSave = true,
            Cancellation = cancel
                ? cancellation
                : null,
        };

        await using var context = CreateContext(database, engine, observation);
        await InitializeAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        // WHY: MySQL's default RepeatableRead is intentionally rejected by the hierarchy's lock protocol.
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken.None);

        await context
            .Set<AncestorIndexReadbackNode>()
            .Where(node => node.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Input, 1005), CancellationToken.None);

        var before = await ReadRowsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var nodes = CreateInputs();
        var inputBefore = nodes
            .Select(node => Capture(node, false))
            .ToArray();

        var branch = CreateBranch(nodes);
        observation.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InsertSubtreeAsync(branch, 1, cancellation.Token));

        observation.Enabled = false;
        var persisted = await ReadRowsAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertFailure(error, cancel);
        AssertReadback(observation, nodes, 0);
        Assert.Equal(before, persisted);
        Assert.Equal(
            inputBefore,
            nodes
                .Select(node => Capture(node, false))
                .ToArray());

        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
        AssertNativeShape(engine, observation, declaredTrigger: false);
    }

    /// <summary>Allows the exact restored input graph to succeed after rollback of a later payload save.</summary>
    /// <param name="engine">The actual Doka server family.</param>
    /// <param name="cancel">Whether the arranged failed import was canceled.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task InsertSubtreeAsync_RestoredAssignedImport_CanBeRetriedAfterLaterReadbackFailure(
        string engine,
        bool cancel
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await using var database = await _fixture.CreateDatabaseAsync(
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null,
            qualifySchema: true);

        var failed = new ReadbackObservation
        {
            FailLaterSave = true,
            Cancellation = cancel
                ? cancellation
                : null,
        };

        await using var context = CreateContext(database, engine, failed);
        await InitializeAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var nodes = CreateInputs();
        var inputBefore = nodes
            .Select(node => Capture(node, false))
            .ToArray();

        var branch = CreateBranch(nodes);
        failed.Enabled = true;
        var priorError = await Record.ExceptionAsync(() => context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InsertSubtreeAsync(branch, 1, cancellation.Token));

        failed.Enabled = false;
        var restored = nodes
            .Select(node => Capture(node, false))
            .ToArray();

        // Act
        await context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InsertSubtreeAsync(branch, 1, CancellationToken.None);

        var persisted = await ReadRowsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var validation = await context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InTree(11)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        AssertFailure(priorError, cancel);
        AssertReadback(failed, nodes, 0);
        Assert.Equal(inputBefore, restored);
        Assert.Equal(ImportCount + 1, persisted.Length);
        Assert.All(nodes, node => Assert.Equal(node.Input * 17L + 3L, node.Stamp));
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.IsBranch));
        Assert.True(validation.IsValid);
        Assert.Empty(context.ChangeTracker.Entries());
        AssertNativeShape(engine, failed, declaredTrigger: false);
    }
}
