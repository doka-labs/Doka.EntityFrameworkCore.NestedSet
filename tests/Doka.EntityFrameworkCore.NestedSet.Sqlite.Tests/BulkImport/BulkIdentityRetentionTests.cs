using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies rejected import callbacks release native identities without clearing unrelated tracking.</summary>
[Collection("Allocation measurements")]
public sealed partial class BulkIdentityRetentionTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the fixture's exact SQLite engine selection.</summary>
    /// <param name="fixture">The provider fixture bound to this suite.</param>
    public BulkIdentityRetentionTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>Replacement of an accepted scalar key leaves no stale Find result after database rollback.</summary>
    /// <param name="generated">Whether EF generates the identity instead of accepting the caller's key.</param>
    /// <param name="afterSave">Whether the callback runs after acceptance instead of before persistence.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ScalarKeyRejectionReleasesInstalledIdentity(
        bool generated,
        bool afterSave
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider.UseNestedSets())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        var installed = 0;

        if (afterSave)
        {
            context.SavedChanges += (_, _) => Mutate();
        }
        else
        {
            context.SavingChanges += (_, _) => Mutate();
        }

        // Act
        var result = await ImportScalarAsync(context, generated);

        // Assert
        Assert.True(result.Error is NestedSetException, result.Error?.ToString());
        Assert.True(installed > 0);
        Assert.True(result.Restored);
        Assert.Null(await context.FindAsync<ScalarNode>([installed], CancellationToken.None));
        Assert.Null(await context.FindAsync<ScalarNode>([99], CancellationToken.None));
        CollectInputs();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<ScalarNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);

        return;

        void Mutate()
        {
            var entry = context
                .ChangeTracker
                .Entries<ScalarNode>()
                .Single();

            installed = (int)entry.Property(nameof(ScalarNode.Id)).CurrentValue!;
            entry.Entity.Id = 99;
        }
    }

    /// <summary>A subsequently attached imported aggregate resolves to the same native identity.</summary>
    [Fact]
    public async Task NativeFindResolvesTheAttachedImportedMutableIdentity()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider.UseNestedSets())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var node = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Payload = new IdentityPayload(),
        };

        await context
            .NestedSet<MutableNode>()
            .ForScope(new MutableIdentity("scope"))
            .InsertForestAsync(
                [new NestedSetTreeImport<MutableNode, Guid>(Guid.Empty, new NestedSetBranch<MutableNode>(node))],
                CancellationToken.None);

        context.Attach(node);

        // Act
        var found = await context.FindAsync<MutableNode>(
            [new MutableIdentity("scope"), new MutableIdentity("node")],
            CancellationToken.None);

        // Assert
        Assert.Same(node, found);
        Assert.Equal(EntityState.Unchanged, context.Entry(node).State);
        Assert.Equal(1, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>Later graph callbacks cannot strand an earlier scalar identity or its intermediate key.</summary>
    /// <param name="intermediate">Whether detection separates two replacements before the callback throws.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterTrackingMutationReleasesEveryIntroducedIdentity(
        bool intermediate
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider.UseNestedSets())
            .Options;

        await using var context = new IdentityContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        context.ChangeTracker.Tracking += (_, args) =>
        {
            if (args.Entry.Entity is not ScalarNode { Id: 2 }
                || args.State != EntityState.Added)
            {
                return;
            }

            var first = context
                .ChangeTracker
                .Entries<ScalarNode>()
                .Single(entry => entry.Entity.Id == 1)
                .Entity;

            first.Id = 99;

            if (intermediate)
            {
                context.ChangeTracker.DetectChanges();
                first.Id = 100;

                throw new InvalidOperationException("tracking-intermediate");
            }
        };

        // Act
        var result = await ImportTwoRootsAsync(context);

        // Assert
        if (intermediate)
        {
            Assert.Equal(
                "tracking-intermediate",
                Assert.IsType<InvalidOperationException>(result.Error).Message);
        }
        else
        {
            Assert.Equal(
                NestedSetErrorCode.InvalidImport,
                Assert.IsType<NestedSetException>(result.Error).Code);
        }

        Assert.True(result.Restored);
        CollectInputs();
        Assert.False(result.First.TryGetTarget(out _));
        Assert.False(result.Second.TryGetTarget(out _));
        Assert.Null(await context.FindAsync<ScalarNode>([1], CancellationToken.None));
        Assert.Null(await context.FindAsync<ScalarNode>([99], CancellationToken.None));
        Assert.Null(await context.FindAsync<ScalarNode>([100], CancellationToken.None));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<ScalarNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }
}
