using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies owned identities and defaults survive import rollback across payload waves.</summary>
public sealed partial class BulkOwnedGeneratedTests : ProviderTest,
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
    public async Task InsertForestAsync_LateFailure_RestoresOwnedDefaultsAndCollectionIdentities(
        int count,
        bool callbackPayload,
        bool prefilledOwnership
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var source = database.CreateContext();
        var probe = new BulkRefreshFailure("OwnedGeneratedImportNodes");
        var options = new DbContextOptionsBuilder<OwnedGeneratedContext>()
            .ConfigureTestWarnings()
            .AddInterceptors(probe)
            .UseSqlite(source.Database.GetConnectionString(), provider => provider
                .CommandTimeout(0)
                .UseNestedSets())
            .Options;

        await using var context = new OwnedGeneratedContext(options);
        FunctionalTestContextPolicy.Configure(context);
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
                    OwnerId = prefilledOwnership
                        ? key + 700
                        : 0,
                    Label = "original",
                },
                Rows =
                [
                    new OwnedGeneratedRow
                    {
                        OwnerId = prefilledOwnership
                            ? key + 800
                            : 0,
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
                Assert.Equal(
                    prefilledOwnership
                        ? node.Id + 700
                        : 0,
                    node.Payload.OwnerId);

                Assert.Equal(0, node.Payload.Revision);
                Assert.Equal(
                    callbackPayload
                        ? "callback"
                        : "original",
                    node.Payload.Label);

                Assert.Equal(
                    callbackPayload
                        ? 123
                        : null,
                    node.Payload.ReferenceId);

                var row = Assert.Single(node.Rows);

                Assert.Equal(0, row.Id);
                Assert.Equal(
                    prefilledOwnership
                        ? node.Id + 800
                        : 0,
                    row.OwnerId);

                Assert.Equal(0, row.Revision);
                Assert.Equal(
                    callbackPayload
                        ? "callback"
                        : "row",
                    row.Label);
            });

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(
            0,
            await context
                .Set<OwnedGeneratedNode>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>A successful import keeps returned owned defaults and collection identities observable.</summary>
    [Fact]
    public async Task InsertForestAsync_SuccessfulImport_ReturnsOwnedDefaultsAndCollectionIdentities()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<OwnedGeneratedContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider
                .CommandTimeout(0)
                .UseNestedSets())
            .Options;

        await using var context = new OwnedGeneratedContext(options);
        FunctionalTestContextPolicy.Configure(context);
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
        Assert.Equal(
            1,
            await context
                .Set<OwnedGeneratedNode>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>A rolled-back aggregate with restored ownership identities can be imported again.</summary>
    [Fact]
    public async Task InsertForestAsync_RestoredOwnedIdentities_AllowImportRetryAcrossPayloadWaves()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var source = database.CreateContext();
        var probe = new BulkRefreshFailure("OwnedGeneratedImportNodes");
        var options = new DbContextOptionsBuilder<OwnedGeneratedContext>()
            .ConfigureTestWarnings()
            .AddInterceptors(probe)
            .UseSqlite(source.Database.GetConnectionString(), provider => provider
                .CommandTimeout(0)
                .UseNestedSets())
            .Options;

        await using var context = new OwnedGeneratedContext(options);
        FunctionalTestContextPolicy.Configure(context);
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
                    },
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
        Assert.Equal(
            65,
            await context
                .Set<OwnedGeneratedNode>()
                .CountAsync(CancellationToken.None));
    }
}
