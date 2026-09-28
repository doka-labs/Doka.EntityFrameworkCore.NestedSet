namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises the convention-owned restrictive parent foreign key during set-based deletion.</summary>
public abstract class SubtreeDeleteWriteTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Reuses provider instances while each test resets its own mapped rows.</summary>
    protected SubtreeDeleteWriteTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// Deletes a deep chain with the derived restrictive self foreign key using only necessary unlinks.
    /// </summary>
    [Fact]
    public async Task ConventionOwnedRestrictiveForeignKeyRemainsSafe()
    {
        // Arrange
        var expectedUnlinks = Engine is "PostgreSql" or "SqlServer" ? 0 : 39;

        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await SeedAsync(setup);
        var probe = new StructuralWriteProbe("ConstrainedNode", "Left", "Right");
        await using var context = CreateContext(setup, probe);
        var service = context
            .NestedSet<ConstrainedNode>()
            .ForScope(1);

        // Act
        await service.DeleteSubtreeAsync(1, CancellationToken.None);

        // Assert
        Assert.Same(setup.GetService<IModelSource>(), context.GetService<IModelSource>());
        var parent = Assert.Single(context.Model.FindEntityType(typeof(ConstrainedNode))!.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        Assert.Equal(
            expectedUnlinks,
            probe
                .NodeUpdates
                .Where(write => SqlAssignments.Assigns(write.Sql, "ParentId"))
                .Sum(write => write.Rows));
        Assert.Empty(
            await service
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<ConstrainedNode>()
                .CountAsync(CancellationToken.None));

        var entityType = context.Model.FindEntityType(typeof(ConstrainedNode))!;
        var registry = NestedSetTreeRegistryMapping.For(entityType).Registry;
        var lifecycle = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Where(row => EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope) == 1
                && EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == Guid.Empty)
            .Select(row => EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle))
            .SingleAsync(CancellationToken.None);

        Assert.Equal(NestedSetTreeRegistryMetadata.Tombstoned, lifecycle);
    }

    /// <summary>
    /// An exception after the set deletion restores every row and parent link inside a caller transaction.
    /// </summary>
    [Fact]
    public async Task FailureAfterPhysicalDeleteRestoresConventionOwnedForeignKeys()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await SeedAsync(setup);
        var probe = new DeleteFailureProbe();
        await using var context = CreateContext(setup, probe);
        await using var transaction = await context.Database.BeginTransactionAsync(
            Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            CancellationToken.None);

        var service = context
            .NestedSet<ConstrainedNode>()
            .ForScope(1);

        var before = await SnapshotAsync(context);

        // Act
        var failure = await Record.ExceptionAsync(() => service.DeleteSubtreeAsync(1, CancellationToken.None));

        // Assert
        Assert.IsType<InjectedDeleteException>(failure);
        Assert.Equal(40, probe.DeletedRows);
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.Same(transaction, context.Database.CurrentTransaction);
    }

    /// <summary>Creates the mutation model over the same convention-owned constrained table.</summary>
    private static ConventionDeleteContext CreateContext(
        TreeContext source,
        DbCommandInterceptor interceptor
    )
    {
        var extensions = source
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        // WHY: Cloning the complete provider setup retains provider services while the distinct context type
        // proves the parent relationship is derived from nested-set metadata rather than fixture model reuse.
        var options = new DbContextOptionsBuilder<ConventionDeleteContext>(
            new DbContextOptions<ConventionDeleteContext>(extensions)).ConfigureTestWarnings();

        options.AddInterceptors(interceptor);

        return new ConventionDeleteContext(options.Options);
    }

    /// <summary>Creates a chain deeper than common cascade limits and an overlapping isolated scope.</summary>
    private static async Task SeedAsync(
        TreeContext context
    )
    {
        for (var key = 1; key <= 40; key++)
        {
            await context.AddAsync(
                new ConstrainedNode
                {
                    Id = key,
                    Tree = 1,
                    ParentId = key == 1 ? null : key - 1,
                    Left = key,
                    Right = 81 - key,
                    Depth = key - 1,
                },
                CancellationToken.None);
        }

        await context.AddAsync(
            new ConstrainedNode
            {
                Id = 100,
                Tree = 2,
                Left = 1,
                Right = 2,
            },
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
    }

    /// <summary>Reads the complete physical rows for exact rollback comparison.</summary>
    private static Task<string[]> SnapshotAsync(
        ConventionDeleteContext context
    ) => context
        .Set<ConstrainedNode>()
        .AsNoTracking()
        .OrderBy(node => node.Id)
        .Select(node =>
            node.Id
            + ":"
            + node.ParentId
            + ":"
            + node.Left
            + ":"
            + node.Right
            + ":"
            + node.Depth
            + ":"
            + node.Position
            + ":"
            + node.Tree)
        .ToArrayAsync(CancellationToken.None);

    /// <summary>
    /// Uses the fixture's constrained physical table and derives its parent foreign key by convention.
    /// </summary>
    private sealed class ConventionDeleteContext : DbContext
    {
        /// <summary>Uses the same provider connection as the fixture.</summary>
        internal ConventionDeleteContext(
            DbContextOptions<ConventionDeleteContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            if (Database.IsNpgsql())
            {
                // WHY: The fixture's physical hierarchy and registry tables live in a nondefault schema.
                modelBuilder.HasDefaultSchema("hierarchies");
            }

            var node = modelBuilder.Entity<ConstrainedNode>();
            node
                .Property(entity => entity.Id)
                .ValueGeneratedNever();
            node.HasNestedSet(builder => builder
                .HasTreeId(entity => entity.TreeId)
                .HasScope(entity => entity.Tree)
                .HasParent(entity => entity.ParentId));

            if (Database.IsNpgsql())
            {
                foreach (var entity in modelBuilder.Model.GetEntityTypes())
                {
                    foreach (var property in entity.GetProperties())
                    {
                        property.SetColumnName(property.Name.ToLowerInvariant());
                    }
                }
            }
        }
    }

    /// <summary>Marks an intentional failure after the server completed its physical DELETE.</summary>
    private sealed class InjectedDeleteException : Exception;

    /// <summary>Proves rollback starts after real row deletion, rather than merely before command execution.</summary>
    private sealed class DeleteFailureProbe : DbCommandInterceptor
    {
        /// <summary>Gets the deleted row count observed before failure.</summary>
        internal int DeletedRows { get; private set; }

        /// <inheritdoc />
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (command
                    .CommandText
                    .TrimStart()
                    .StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("ConstrainedNode", StringComparison.OrdinalIgnoreCase))
            {
                DeletedRows = result;
                throw new InjectedDeleteException();
            }

            return ValueTask.FromResult(result);
        }
    }
}
