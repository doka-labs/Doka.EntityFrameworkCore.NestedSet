using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkIdentityRetentionTests
{
    /// <summary>Mutable converted key or scope changes release root and owned identity maps after rejection.</summary>
    /// <param name="scope">Whether the callback mutates the composite scope instead of the node identity.</param>
    /// <param name="binary">Whether the mutable representation is a byte buffer instead of a converted class.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MutableKeyRejectionReleasesRootAndOwnedIdentities(
        bool scope,
        bool binary
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
        context.SavedChanges += (_, _) =>
        {
            if (binary)
            {
                var node = context
                    .ChangeTracker
                    .Entries<BinaryNode>()
                    .Single()
                    .Entity;

                (scope ? node.Scope : node.Id)[0] = 4;
            }
            else
            {
                var node = context
                    .ChangeTracker
                    .Entries<MutableNode>()
                    .Single()
                    .Entity;

                (scope ? node.Scope : node.Id).Value = "changed";
            }
        };

        // Act
        var result = await ImportMutableAsync(context, binary);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(result.Error).Code);
        Assert.True(result.Restored);
        CollectInputs();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.False(result.Payload.TryGetTarget(out _));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.Set<BinaryNode>().CountAsync(CancellationToken.None));
        Assert.Null(
            await context.FindAsync<MutableNode>(
                [new MutableIdentity("scope"), new MutableIdentity("node")],
                CancellationToken.None));

        Assert.Null(await context.FindAsync<BinaryNode>([new byte[] { 1 }, new byte[] { 3 }], CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>Shallow EF binary snapshots do not cause a false cleanup failure for one rejected mutation.</summary>
    /// <param name="scope">Whether the callback changes the scope instead of the node identity.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShallowBinarySnapshotsReleaseRejectedIdentitiesWithoutDiscard(
        bool scope
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<ShallowBinaryContext>()
            .ConfigureTestWarnings()
            .UseSqlite(source.Database.GetConnectionString(), provider => provider.UseNestedSets())
            .Options;

        await using var context = new ShallowBinaryContext(options);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        context.SavedChanges += (_, _) =>
        {
            var input = context
                .ChangeTracker
                .Entries<BinaryNode>()
                .Single()
                .Entity;

            (scope ? input.Scope : input.Id)[0] = 4;
        };

        // Act
        var result = await ImportMutableAsync(context, binary: true);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(result.Error).Code);
        Assert.True(result.Restored);
        CollectInputs();
        Assert.False(result.Input.TryGetTarget(out _));
        Assert.False(result.Payload.TryGetTarget(out _));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Null(await context.FindAsync<BinaryNode>([new byte[] { 1 }, new byte[] { 3 }], CancellationToken.None));
        Assert.Equal(0, await context.Set<BinaryNode>().CountAsync(CancellationToken.None));
        GC.KeepAlive(context);
    }

    /// <summary>A successful import preserves mutable identities while releasing its native tracking.</summary>
    [Fact]
    public async Task SuccessfulImportReleasesMutableIdentities()
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

        // Act
        await context
            .NestedSet<MutableNode>()
            .ForScope(new MutableIdentity("scope"))
            .InsertForestAsync(
                [new NestedSetTreeImport<MutableNode, Guid>(Guid.Empty, new NestedSetBranch<MutableNode>(node))],
                CancellationToken.None);

        // Assert
        Assert.Equal("node", node.Id.Value);
        Assert.Equal("scope", node.Scope.Value);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(1, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>Comparer-equal replacements around explicit detection preserve a valid mutable-key insertion.</summary>
    /// <param name="scope">Whether replacements affect the configured scope instead of the node key.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentIdentityReplacementsRemainValidAroundExplicitDetection(
        bool scope
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

        var node = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Payload = new IdentityPayload(),
        };

        context.SavingChanges += (_, _) =>
        {
            var input = context
                .ChangeTracker
                .Entries<MutableNode>()
                .Single()
                .Entity;

            if (scope)
            {
                input.Scope = new MutableIdentity("scope");
            }
            else
            {
                input.Id = new MutableIdentity("node");
            }

            context.ChangeTracker.DetectChanges();

            if (scope)
            {
                input.Scope = new MutableIdentity("scope");
            }
            else
            {
                input.Id = new MutableIdentity("node");
            }
        };

        // Act
        await context
            .NestedSet<MutableNode>()
            .ForScope(new MutableIdentity("scope"))
            .InsertForestAsync(
                [new NestedSetTreeImport<MutableNode, Guid>(Guid.Empty, new NestedSetBranch<MutableNode>(node))],
                CancellationToken.None);

        // Assert
        Assert.Equal("node", node.Id.Value);
        Assert.Equal("scope", node.Scope.Value);
        Assert.Equal(1, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
