using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies owned identities and defaults survive import rollback across payload waves.</summary>
public sealed class BulkOwnedGeneratedTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the provider-owned SQLite fixture.</summary>
    /// <param name="fixture">The fixture bound to this suite.</param>
    public BulkOwnedGeneratedTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>A late failure restores owned identities and defaults while preserving callback payload.</summary>
    /// <param name="count">The number of roots, including cases that exceed one bounded payload wave.</param>
    /// <param name="callbackPayload">Whether callbacks change ordinary owned payload preserved by rollback.</param>
    /// <param name="prefilledOwnership">Whether ownership starts with non-sentinel caller CLR values.</param>
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(65, false, true)]
    [InlineData(65, true, true)]
    public async Task LateFailureRestoresOwnedDefaultsAndCollectionIdentities(
        int count,
        bool callbackPayload,
        bool prefilledOwnership
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var probe = new BulkRefreshFailure("OwnedGeneratedImportNodes");
        var options = new DbContextOptionsBuilder<OwnedGeneratedContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .AddInterceptors(probe)
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new OwnedGeneratedContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var nodes = Enumerable
            .Range(1, count)
            .Select(key => new OwnedGeneratedNode
            {
                Id = key,
                Payload = new OwnedGeneratedPayload
                {
                    OwnerId = prefilledOwnership ? key + 700 : 0,
                    Label = "original",
                },
                Rows =
                [
                    new OwnedGeneratedRow
                    {
                        OwnerId = prefilledOwnership ? key + 800 : 0,
                        Label = "row",
                    },
                ],
            })
            .ToArray();

        var imports = nodes
            .Select(node => new NestedSetTreeImport<OwnedGeneratedNode, Guid>(
                Guid.NewGuid(),
                new NestedSetBranch<OwnedGeneratedNode>(node)))
            .ToArray();

        var generatedObserved = false;
        context.SavedChanges += (_, _) =>
        {
            generatedObserved |= context
                .ChangeTracker
                .Entries<OwnedGeneratedPayload>()
                .Any(entry => entry.Property(nameof(OwnedGeneratedPayload.Revision))
                    .CurrentValue is 43);

            if (callbackPayload)
            {
                foreach (var entry in context.ChangeTracker.Entries<OwnedGeneratedPayload>())
                {
                    entry.Entity.Label = "callback";
                    entry.Entity.ReferenceId = 123;
                }

                foreach (var entry in context.ChangeTracker.Entries<OwnedGeneratedRow>())
                {
                    entry.Entity.Label = "callback";
                }
            }

            probe.Inserted = true;
        };

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<OwnedGeneratedNode>()
            .InsertForestAsync(imports, CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.True(probe.ReachedRefresh);
        Assert.True(generatedObserved);
        Assert.All(
            nodes,
            node =>
            {
                Assert.Equal(0, node.Revision);
                Assert.Equal(prefilledOwnership ? node.Id + 700 : 0, node.Payload.OwnerId);
                Assert.Equal(0, node.Payload.Revision);
                Assert.Equal(callbackPayload ? "callback" : "original", node.Payload.Label);
                Assert.Equal(callbackPayload ? 123 : (int?)null, node.Payload.ReferenceId);
                var row = Assert.Single(node.Rows);

                Assert.Equal(0, row.Id);
                Assert.Equal(prefilledOwnership ? node.Id + 800 : 0, row.OwnerId);
                Assert.Equal(0, row.Revision);
                Assert.Equal(callbackPayload ? "callback" : "row", row.Label);
            });

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0, await context.Set<OwnedGeneratedNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>A successful import keeps returned owned defaults and collection identities observable.</summary>
    [Fact]
    public async Task SuccessfulImportReturnsOwnedDefaultsAndCollectionIdentities()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<OwnedGeneratedContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new OwnedGeneratedContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var node = new OwnedGeneratedNode
        {
            Id = 1,
            Payload = new OwnedGeneratedPayload(),
            Rows = [new OwnedGeneratedRow()],
        };

        // Act
        await context
            .NestedSet<OwnedGeneratedNode>()
            .InsertForestAsync(
                [
                    new NestedSetTreeImport<OwnedGeneratedNode, Guid>(
                        Guid.Empty,
                        new NestedSetBranch<OwnedGeneratedNode>(node)),
                ],
                CancellationToken.None);

        // Assert
        Assert.Equal(42, node.Revision);
        Assert.Equal(node.Id, node.Payload.OwnerId);
        Assert.Equal(43, node.Payload.Revision);
        var row = Assert.Single(node.Rows);

        Assert.True(row.Id > 0);
        Assert.Equal(node.Id, row.OwnerId);
        Assert.Equal(43, row.Revision);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(1, await context.Set<OwnedGeneratedNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>A rolled-back aggregate with restored ownership identities can be imported again.</summary>
    [Fact]
    public async Task RestoredOwnedIdentitiesAllowImportRetryAcrossPayloadWaves()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var probe = new BulkRefreshFailure("OwnedGeneratedImportNodes");
        var options = new DbContextOptionsBuilder<OwnedGeneratedContext>()
            .ConfigureTestWarnings()
            .UseNestedSets()
            .AddInterceptors(probe)
            .UseSqlite(source.Database.GetConnectionString())
            .Options;

        await using var context = new OwnedGeneratedContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var nodes = Enumerable
            .Range(1, 65)
            .Select(key => new OwnedGeneratedNode
            {
                Id = key,
                Payload = new OwnedGeneratedPayload
                {
                    OwnerId = key + 700,
                    Label = "original",
                },
                Rows =
                [
                    new OwnedGeneratedRow
                    {
                        OwnerId = key + 800,
                        Label = "row",
                    }
                ],
            })
            .ToArray();

        var imports = nodes
            .Select(node => new NestedSetTreeImport<OwnedGeneratedNode, Guid>(
                Guid.NewGuid(),
                new NestedSetBranch<OwnedGeneratedNode>(node)))
            .ToArray();

        context.SavedChanges += (_, _) => probe.Inserted = true;
        var priorError = await Record.ExceptionAsync(() => context
            .NestedSet<OwnedGeneratedNode>()
            .InsertForestAsync(imports, CancellationToken.None));

        // Act
        await context
            .NestedSet<OwnedGeneratedNode>()
            .InsertForestAsync(imports, CancellationToken.None);

        // Assert
        Assert.IsType<InjectedCommandException>(priorError);
        Assert.True(probe.ReachedRefresh);
        Assert.All(
            nodes,
            node =>
            {
                Assert.Equal(42, node.Revision);
                Assert.Equal(node.Id, node.Payload.OwnerId);
                Assert.Equal(43, node.Payload.Revision);
                Assert.Equal("original", node.Payload.Label);
                var row = Assert.Single(node.Rows);

                Assert.True(row.Id > 0);
                Assert.Equal(node.Id, row.OwnerId);
                Assert.Equal(43, row.Revision);
                Assert.Equal("row", row.Label);
            });

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(65, await context.Set<OwnedGeneratedNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>Maps separate generated owned rows together with a table-sharing generated owned payload.</summary>
    private sealed class OwnedGeneratedContext(DbContextOptions<OwnedGeneratedContext> options)
        : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            var node = builder.Entity<OwnedGeneratedNode>();
            node.ToTable("OwnedGeneratedImportNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node
                .Property(value => value.Revision)
                .HasDefaultValue(42);
            node.HasNestedSet(set => set
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .HasBounds(value => value.Left, value => value.Right)
                .HasDepth(value => value.Depth)
                .HasPosition(value => value.Position));

            node.OwnsOne(
                value => value.Payload,
                payload =>
                {
                    payload
                        .WithOwner()
                        .HasForeignKey(value => value.OwnerId);
                    payload
                        .Property(value => value.OwnerId)
                        .ValueGeneratedNever();
                    payload
                        .Property(value => value.Revision)
                        .HasDefaultValue(43);
                    payload
                        .HasOne<OwnedReference>()
                        .WithMany()
                        .HasForeignKey(value => value.ReferenceId);
                });

            node.OwnsMany(
                value => value.Rows,
                rows =>
                {
                    rows.ToTable("OwnedGeneratedImportRows");
                    rows
                        .WithOwner()
                        .HasForeignKey(value => value.OwnerId);
                    rows
                        .Property(value => value.OwnerId)
                        .ValueGeneratedNever();
                    rows.HasKey(value => value.Id);
                    rows
                        .Property(value => value.Id)
                        .ValueGeneratedOnAdd();
                    rows
                        .Property(value => value.Revision)
                        .HasDefaultValue(43);
                });
        }
    }

    /// <summary>Provides caller-owned root structure and a generated default.</summary>
    private sealed class OwnedGeneratedNode
    {
        /// <summary>Gets or sets the assigned node identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left nested-set boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right nested-set boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth relative to the root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the database-generated root default.</summary>
        public int Revision { get; set; }

        /// <summary>Gets or sets the generated table-sharing owned payload.</summary>
        public OwnedGeneratedPayload Payload { get; set; } = new();

        /// <summary>Gets or sets the owned collection containing database-generated identities.</summary>
        public List<OwnedGeneratedRow> Rows { get; set; } = [];
    }

    /// <summary>Provides a database-generated non-key default within a table-sharing owned entity.</summary>
    private sealed class OwnedGeneratedPayload
    {
        /// <summary>Gets or sets the public ownership foreign and primary identity.</summary>
        public int OwnerId { get; set; }

        /// <summary>Gets or sets an ordinary business foreign key controlled by application callbacks.</summary>
        public int? ReferenceId { get; set; }

        /// <summary>Gets or sets the generated owned default.</summary>
        public int Revision { get; set; }

        /// <summary>Gets or sets ordinary caller-owned payload.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Provides both a generated owned collection identity and a generated ordinary scalar.</summary>
    private sealed class OwnedGeneratedRow
    {
        /// <summary>Gets or sets the public ownership foreign identity.</summary>
        public int OwnerId { get; set; }

        /// <summary>Gets or sets the generated owned-row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the generated owned-row default.</summary>
        public int Revision { get; set; }

        /// <summary>Gets or sets ordinary caller-owned row payload.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Provides a business principal whose foreign key is separate from aggregate ownership.</summary>
    private sealed class OwnedReference
    {
        /// <summary>Gets or sets the business principal identity.</summary>
        public int Id { get; set; }
    }
}
