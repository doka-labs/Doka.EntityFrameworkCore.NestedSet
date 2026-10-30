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
    public async Task AssignedSiblingImportPreservesNativeGeneratedAttribution(
        string engine
    )
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var observation = new ReadbackObservation();
        await using var context = CreateContext(database, engine, observation);
        await InitializeAsync(context);
        var nodes = CreateInputs();
        var branch = CreateBranch(nodes);
        observation.Enabled = true;

        // Act
        await context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InsertSubtreeAsync(branch, 1, CancellationToken.None);

        observation.Enabled = false;
        var persisted = await ReadRowsAsync(context);
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
    public async Task DeclaredTriggerImportKeepsSafeReadbackFallback()
    {
        // Arrange
        await using var database = await _fixture.CreateDatabaseAsync("MySql");
        var observation = new ReadbackObservation();
        await using var context = CreateContext(database, "MySql", observation, declaredTrigger: true);
        await InitializeAsync(context);
        var nodes = CreateInputs();
        var branch = CreateBranch(nodes);
        observation.Enabled = true;

        // Act
        await context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InsertSubtreeAsync(branch, 1, CancellationToken.None);

        observation.Enabled = false;
        var persisted = await ReadRowsAsync(context);

        // Assert
        AssertReadback(observation, nodes, 100);
        Assert.Equal(ImportCount + 1, persisted.Length);
        // WHY: Input remains application-owned; the import refreshes generated values rather than replacing
        // arbitrary payload with trigger changes. Validate both sides independently instead of inferring one.
        Assert.Equal(Enumerable.Range(1, ImportCount), nodes.Select(node => node.Input));
        Assert.All(nodes, node =>
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
    public async Task LaterReadbackFailureRestoresCallerTransactionAndInputs(
        string engine,
        bool cancel
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var observation = new ReadbackObservation
        {
            FailLaterSave = true,
            Cancellation = cancel ? cancellation : null,
        };

        await using var context = CreateContext(database, engine, observation);
        await InitializeAsync(context);

        // WHY: MySQL's default RepeatableRead is intentionally rejected by the hierarchy's lock protocol.
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken.None);

        await context
            .Set<AncestorIndexReadbackNode>()
            .Where(node => node.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Input, 1005), CancellationToken.None);

        var before = await ReadRowsAsync(context);
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
        var persisted = await ReadRowsAsync(context);

        // Assert
        AssertFailure(error, cancel);
        AssertReadback(observation, nodes, 0);
        Assert.Equal(before, persisted);
        Assert.Equal(inputBefore, nodes.Select(node => Capture(node, false)).ToArray());
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
    public async Task RestoredAssignedImportCanBeRetriedAfterLaterReadbackFailure(
        string engine,
        bool cancel
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await using var database = await _fixture.CreateDatabaseAsync(engine);
        var failed = new ReadbackObservation
        {
            FailLaterSave = true,
            Cancellation = cancel ? cancellation : null
        };

        await using var context = CreateContext(database, engine, failed);
        await InitializeAsync(context);
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

        var persisted = await ReadRowsAsync(context);
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

    /// <summary>Uses the existing isolated database and its actual configured Doka engine.</summary>
    private static AncestorIndexReadbackContext CreateContext(
        MigrationDatabase database,
        string engine,
        ReadbackObservation observation,
        bool declaredTrigger = false
    )
    {
        using var connectionContext = database.CreateContext();
        var connection = connectionContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The migration fixture did not configure a connection string.");

        var version = engine == "MariaDb" ? DatabaseTestTargets.MariaDb : DatabaseTestTargets.MySql;
        var options = new DbContextOptionsBuilder<AncestorIndexReadbackContext>()
            .UseMySql(
                connection,
                version,
                mysql => mysql
                    .UseNestedSets()
                    .MaxBatchSize(1000))
            .ReplaceService<IModelCacheKeyFactory, AncestorIndexReadbackModelCacheKeyFactory>()
            .AddInterceptors(new ReadbackCommandProbe(observation), new ReadbackSaveProbe(observation));

        return new AncestorIndexReadbackContext(options.Options, declaredTrigger);
    }

    /// <summary>Creates the real optional schema and an existing parent through the public mutation facade.</summary>
    private static async Task InitializeAsync(
        AncestorIndexReadbackContext context
    )
    {
        await context.Database.EnsureCreatedAsync(CancellationToken.None);

        if (context.DeclaredTrigger)
        {
            await context.Database.ExecuteSqlRawAsync(
                $"CREATE TRIGGER `{AncestorIndexReadbackContext.TriggerName}` BEFORE INSERT "
                + $"ON `{AncestorIndexReadbackContext.TableName}` FOR EACH ROW SET NEW.`Input` = NEW.`Input` + 100;",
                CancellationToken.None);
        }

        await context
            .NestedSet<AncestorIndexReadbackNode>()
            .ForScope(7)
            .InsertRootAsync(
                new AncestorIndexReadbackNode
                {
                    Id = 1,
                    Input = 1000,
                },
                11,
                CancellationToken.None);
    }

    /// <summary>Reverses key and input ordering so incorrect generated-result correlation cannot pass.</summary>
    private static AncestorIndexReadbackNode[] CreateInputs() => Enumerable
        .Range(0, ImportCount)
        .Select(index => new AncestorIndexReadbackNode
        {
            Id = 100 + ImportCount - index,
            Scope = 88,
            TreeId = 99,
            Left = 71,
            Right = 72,
            Depth = 9,
            Position = 4,
            Input = index + 1,
            Stamp = -9000 - index,
        })
        .ToArray();

    /// <summary>Keeps enough same-shape siblings to exercise multiple bounded public insertion saves.</summary>
    private static NestedSetBranch<AncestorIndexReadbackNode> CreateBranch(
        AncestorIndexReadbackNode[] nodes
    ) => new(
        nodes[0],
        nodes
            .Skip(1)
            .Select(node => new NestedSetBranch<AncestorIndexReadbackNode>(node))
            .ToArray());

    /// <summary>Reads generated shadow values independently of the detached input graph.</summary>
    private static Task<ReadbackRow[]> ReadRowsAsync(
        AncestorIndexReadbackContext context
    ) => context
        .Set<AncestorIndexReadbackNode>()
        .AsNoTracking()
        .OrderBy(node => node.Id)
        .Select(node => new ReadbackRow(
            node.Id,
            node.Scope,
            node.TreeId,
            node.ParentId,
            node.Left,
            node.Right,
            node.Depth,
            node.Position,
            node.Input,
            node.Stamp,
            EF.Property<bool>(node, AncestorIndexReadbackContext.FlagProperty)))
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Captures caller-visible scalar values without attaching the input entity.</summary>
    private static ReadbackRow Capture(
        AncestorIndexReadbackNode node,
        bool isBranch
    ) => new(
        node.Id,
        node.Scope,
        node.TreeId,
        node.ParentId,
        node.Left,
        node.Right,
        node.Depth,
        node.Position,
        node.Input,
        node.Stamp,
        isBranch);

    /// <summary>Checks the actual generated sidecars before a later refresh could repair wrong attribution.</summary>
    private static void AssertReadback(
        ReadbackObservation observation,
        AncestorIndexReadbackNode[] nodes,
        int inputIncrement
    )
    {
        Assert.True(observation.PayloadSaves >= 2);
        Assert.Equal(nodes.Length, observation.SavedRows.Count);
        Assert.Equal(nodes.Length, observation.SavedRows.Select(row => row.Id).Distinct().Count());
        Assert.All(observation.SavedRows, row =>
        {
            Assert.Equal(row.Input * 17L + inputIncrement * 17L + 3L, row.Stamp);
            Assert.Equal(row.Id == nodes[0].Id, row.IsBranch);
        });
    }

    /// <summary>Checks the expected failure type without accepting an unrelated rollback error.</summary>
    private void AssertFailure(
        Exception? error,
        bool cancel
    )
    {
        _output.WriteLine("Observed import failure: " + error);

        if (cancel)
        {
            Assert.IsType<OperationCanceledException>(error, exactMatch: false);

            return;
        }

        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(ReadbackObservation.FailureMessage, failure.Message);
    }

    /// <summary>Validates the observed native format without making an optional provider optimization mandatory.</summary>
    private void AssertNativeShape(
        string engine,
        ReadbackObservation observation,
        bool declaredTrigger
    )
    {
        var inserts = observation.Commands.Sum(command => CountOccurrences(command, "INSERT INTO"));
        var unions = observation.Commands.Sum(command => CountOccurrences(command, "UNION ALL"));
        var returning = observation.Commands.Sum(command => CountOccurrences(command, "RETURNING"));
        _output.WriteLine(
            $"Engine={engine}; rows={observation.SavedRows.Count}; payloadSaves={observation.PayloadSaves}; "
            + $"commands={observation.Commands.Count}; inserts={inserts}; unions={unions}; returning={returning}.");

        Assert.NotEmpty(observation.Commands);
        Assert.InRange(inserts, 1, ImportCount);

        if (engine == "MariaDb")
        {
            Assert.Equal(inserts, returning);
            Assert.Equal(0, unions);
        }
        else
        {
            Assert.Equal(0, returning);

            // WHY: Published Doka retains a correct per-row fallback. The final qualification separately proves
            // selection of its newer grouped path from these real counts, without coupling unrelated API markers.
            Assert.Equal(unions == 0 ? ImportCount : ImportCount - unions, inserts);
        }

        if (declaredTrigger)
        {
            Assert.Equal(ImportCount, inserts);
            Assert.Equal(0, unions);
        }

        _output.WriteLine("First native readback command: " + observation.Commands[0]);
    }

    /// <summary>Counts actual statement markers without changing the command sent to the server.</summary>
    private static int CountOccurrences(
        string text,
        string marker
    )
    {
        var count = 0;
        var offset = 0;

        while ((offset = text.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }

        return count;
    }
}
