namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards mutation boundaries against configured and already executing EF retry strategies.</summary>
public abstract class RetryBoundaryTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Creates cases over the existing real-provider ordering fixture.</summary>
    protected RetryBoundaryTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Rejects replayable service work before any SQL or owned transaction begins.</summary>
    [Theory]
    [InlineData("direct")]
    [InlineData("outer")]
    [InlineData("external")]
    public async Task ServiceMutationRejectsConfiguredAndOuterRetries(
        string mode
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await using var services = RetryBoundaryServices.Create(Engine);
        var commands = new EnterpriseProbe();
        var transactions = new RetryBoundaryTransactionProbe();
        await using var context = Create(setup, mode == "external" ? null : services, commands, transactions);
        await using var outer = Create(setup, services);
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var strategy = (mode == "external" ? outer : context).Database.CreateExecutionStrategy();

        // Act
        var failure = await Record.ExceptionAsync(() => mode == "direct"
            ? tree.InsertRootAsync(
                new OrderingNode
                {
                    Id = 1,
                    Name = "Alpha",
                },
                Guid.Empty,
                CancellationToken.None)
            : strategy.ExecuteAsync(
                token => tree.InsertRootAsync(
                    new OrderingNode
                    {
                        Id = 1,
                        Name = "Alpha",
                    },
                    Guid.Empty,
                    token),
                CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidTransaction, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Empty(commands.Commands);
        Assert.Equal(0, transactions.Starts);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(
            await setup
                .Set<OrderingNode>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Preserves pending order changes while rejecting replay before scope reads, locks, or payload SQL.
    /// </summary>
    [Theory]
    [InlineData("direct", true)]
    [InlineData("direct", false)]
    [InlineData("outer", true)]
    [InlineData("outer", false)]
    [InlineData("external", true)]
    [InlineData("external", false)]
    public async Task OrderedSaveRejectsConfiguredAndOuterRetriesWithoutAcceptingChanges(
        string mode,
        bool acceptChanges
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        await using var services = RetryBoundaryServices.Create(Engine);
        var commands = new EnterpriseProbe();
        var transactions = new RetryBoundaryTransactionProbe();
        await using var context = Create(setup, mode == "external" ? null : services, commands, transactions);
        await using var outer = Create(setup, services);
        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(row => row.Id == 1, CancellationToken.None);

        renamed.Name = "Zulu";
        commands.Commands.Clear();
        var strategy = (mode == "external" ? outer : context).Database.CreateExecutionStrategy();

        // Act
        var failure = await Record.ExceptionAsync(() => mode == "direct"
            ? context.SaveChangesAsync(acceptChanges, CancellationToken.None)
            : strategy.ExecuteAsync(token => context.SaveChangesAsync(acceptChanges, token), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidTransaction, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Empty(commands.Commands);
        Assert.Equal(0, transactions.Starts);
        Assert.Equal(EntityState.Modified, context.Entry(renamed).State);
        Assert.Equal("Zulu", renamed.Name);
        Assert.Equal(
            "Alpha",
            context
                .Entry(renamed)
                .Property(row => row.Name)
                .OriginalValue);
        Assert.Equal(
            "Alpha",
            await setup
                .Set<OrderingNode>()
                .Where(row => row.Id == 1)
                .Select(row => row.Name)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Rejects an ambient coordinated save while preserving application-owned pending values.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrderedSaveRejectsAmbientTransactionsBeforeSql(
        bool acceptChanges
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        var commands = new EnterpriseProbe();
        var transactions = new RetryBoundaryTransactionProbe();
        await using var context = Create(setup, null, commands, transactions);
        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(row => row.Id == 1, CancellationToken.None);

        renamed.Name = "Zulu";
        commands.Commands.Clear();
        Exception? failure;

        // Act
        using (var ambient = new System.Transactions.TransactionScope(
                   System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(
                acceptChanges,
                CancellationToken.None));
        }

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidTransaction, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Empty(commands.Commands);
        Assert.Equal(0, transactions.Starts);
        Assert.Equal(EntityState.Modified, context.Entry(renamed).State);
        Assert.Equal("Zulu", renamed.Name);
        Assert.Equal(
            "Alpha",
            await setup
                .Set<OrderingNode>()
                .Where(row => row.Id == 1)
                .Select(row => row.Name)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Leaves native outer-strategy retry intact when the save contains no hierarchy ordering changes.
    /// </summary>
    [Fact]
    public async Task OuterStrategyStillRetriesAnUnrelatedSave()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await using var services = RetryBoundaryServices.Create(Engine);
        var probe = new SaveBoundaryProbe { FailNextPayload = true };
        await using var context = Create(setup, services, probe);
        await context.AddAsync(
            new OrderingMarker
            {
                Id = 1,
                Value = "ordinary",
            },
            CancellationToken.None);

        var strategy = context.Database.CreateExecutionStrategy();

        // Act
        var saved = await strategy.ExecuteAsync(context.SaveChangesAsync, CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal(2, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
        Assert.Equal(
            "ordinary",
            await setup
                .Set<OrderingMarker>()
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>An outer EF strategy with no retries remains safe for a hierarchy operation.</summary>
    [Fact]
    public async Task NonRetryingOuterStrategyAllowsHierarchyWrites()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await using var context = Create(setup, null);
        var strategy = new RetryBoundaryNonRetryStrategy(context);
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        // Act
        await strategy.ExecuteAsync(
            token => tree.InsertRootAsync(
                new OrderingNode
                {
                    Id = 1,
                    Name = "Alpha",
                },
                Guid.Empty,
                token),
            CancellationToken.None);

        // Assert
        Assert.Equal(
            1,
            await setup
                .Set<OrderingNode>()
                .CountAsync(CancellationToken.None));
        Assert.Empty(
            (await setup
                .NestedSet<OrderingNode>()
                .ForScope(1)
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Replays hierarchy and domain writes through one application-owned transaction boundary.</summary>
    [Fact]
    public async Task RetryingUnitUsesCallerTransactionForHierarchyAndDomainWrites()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await using var services = RetryBoundaryServices.Create(Engine);
        await using var strategyContext = Create(setup, services);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        var attempts = 0;
        var retainedOwnership = false;

        // Act
        await strategy.ExecuteAsync(
            async token =>
            {
                attempts++;
                await using var context = Create(setup, services);
                await using var transaction = await context.Database.BeginTransactionAsync(
                    Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                    token);

                var tree = context
                    .NestedSet<OrderingNode>()
                    .ForScope(1);

                await tree.InsertRootAsync(
                    new OrderingNode
                    {
                        Id = 1,
                        Name = "Alpha",
                    },
                    Guid.Empty,
                    token);

                await context.AddAsync(
                    new OrderingMarker
                    {
                        Id = 1,
                        Value = "atomic domain write",
                    },
                    token);

                await context.SaveChangesAsync(token);
                retainedOwnership = ReferenceEquals(transaction, context.Database.CurrentTransaction);

                if (attempts == 1)
                {
                    // WHY: The deterministic transient failure proves the complete delegate, including its transaction,
                    // is replayed. A failure inside an individual nested-set command would test the wrong retry boundary.
                    throw new SaveBoundaryTransientException();
                }

                await transaction.CommitAsync(token);
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(2, attempts);
        Assert.True(retainedOwnership);
        Assert.Equal(
            "Alpha",
            await setup
                .Set<OrderingNode>()
                .Select(node => node.Name)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(
            "atomic domain write",
            await setup
                .Set<OrderingMarker>()
                .Select(marker => marker.Value)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Rejects a configured retry policy when its caller transaction lives outside a retry delegate.</summary>
    [Fact]
    public async Task CallerTransactionRequiresRetryStrategyBoundary()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await using var services = RetryBoundaryServices.Create(Engine);
        var commands = new EnterpriseProbe();
        await using var context = Create(setup, services, commands);
        await using var transaction = await context.Database.BeginTransactionAsync(
            Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            CancellationToken.None);

        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        commands.Commands.Clear();

        // Act
        var failure = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Alpha",
            },
            Guid.Empty,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidTransaction, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Empty(commands.Commands);
        Assert.Empty(
            await setup
                .Set<OrderingNode>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Preserves the real provider options while explicitly owning intentional retry-service variants.
    /// </summary>
    private static SaveBoundaryContext Create(
        DbContext source,
        IServiceProvider? services,
        params IInterceptor[] interceptors
    )
    {
        var extensions = source
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder<SaveBoundaryContext>(
                new DbContextOptions<SaveBoundaryContext>(extensions))
            .ConfigureTestWarnings()
            .AddInterceptors(interceptors);

        if (services is not null)
        {
            options.UseInternalServiceProvider(services);
        }

        return new SaveBoundaryContext(options.Options);
    }

    /// <summary>Seeds two siblings whose rename requires automatic reordering inside one tree.</summary>
    private static async Task SeedAsync(
        DbContext context
    )
    {
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        await tree.InsertRootAsync(
            new OrderingNode
            {
                Id = 100,
                Name = "Root",
            },
            Guid.Empty,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Alpha",
            },
            100,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 2,
                Name = "Bravo",
            },
            100,
            CancellationToken.None);
    }
}
