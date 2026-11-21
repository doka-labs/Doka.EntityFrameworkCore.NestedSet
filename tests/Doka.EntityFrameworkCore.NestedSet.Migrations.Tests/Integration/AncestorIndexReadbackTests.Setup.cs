namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexReadbackTests
{
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

        var version = engine == "MariaDb"
            ? DatabaseTestTargets.MariaDb
            : DatabaseTestTargets.MySql;

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
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static async Task InitializeAsync(
        AncestorIndexReadbackContext context,
        CancellationToken cancellationToken
    )
    {
        await context.Database.EnsureCreatedAsync(cancellationToken);

        if (context.DeclaredTrigger)
        {
            await context.Database.ExecuteSqlRawAsync(
                $"CREATE TRIGGER `{AncestorIndexReadbackContext.TriggerName}` BEFORE INSERT "
                + $"ON `{AncestorIndexReadbackContext.TableName}` FOR EACH ROW SET NEW.`Input` = NEW.`Input` + 100;",
                cancellationToken);
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
                cancellationToken);
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
}
