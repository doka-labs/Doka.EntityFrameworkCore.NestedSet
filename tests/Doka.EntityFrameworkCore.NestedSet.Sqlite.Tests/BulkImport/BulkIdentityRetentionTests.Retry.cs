using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkIdentityRetentionTests
{
    /// <summary>A rejected mutable principal key preserves its exact owned payload for a subsequent import.</summary>
    /// <param name="scope">Whether the callback changes the composite scope instead of the node key.</param>
    /// <param name="binary">Whether the representation is a binary buffer instead of a converted class.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MutableKeyRejectionPreservesOwnedPayloadForRetry(
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

        var payload = new IdentityPayload { Label = "kept" };
        var bufferNode = new BinaryNode
        {
            Id = [3],
            Scope = [8],
            Payload = payload,
        };

        var classNode = new MutableNode
        {
            Id = new MutableIdentity("node"),
            Payload = payload,
        };

        var mutated = false;
        context.SavedChanges += (_, _) =>
        {
            if (mutated)
            {
                return;
            }

            mutated = true;

            if (binary)
            {
                var input = context
                    .ChangeTracker
                    .Entries<BinaryNode>()
                    .Single()
                    .Entity;

                (scope ? input.Scope : input.Id)[0] = 4;
            }
            else
            {
                var input = context
                    .ChangeTracker
                    .Entries<MutableNode>()
                    .Single()
                    .Entity;

                (scope ? input.Scope : input.Id).Value = "changed";
            }
        };

        var rejection = await Record.ExceptionAsync(Import);

        // Act
        await Import();

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(rejection).Code);
        Assert.Same(payload, binary ? bufferNode.Payload : classNode.Payload);
        Assert.Equal("kept", payload.Label);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(binary ? 1 : 0, await context.Set<BinaryNode>().CountAsync(CancellationToken.None));
        Assert.Equal(binary ? 0 : 1, await context.Set<MutableNode>().CountAsync(CancellationToken.None));

        return;

        Task Import() => binary
            ? context
                .NestedSet<BinaryNode>()
                .ForScope<byte[]>([1])
                .InsertForestAsync(
                    [
                        new NestedSetTreeImport<BinaryNode, Guid>(
                            Guid.Empty,
                            new NestedSetBranch<BinaryNode>(bufferNode))
                    ],
                    CancellationToken.None)
            : context
                .NestedSet<MutableNode>()
                .ForScope(new MutableIdentity("scope"))
                .InsertForestAsync(
                    [
                        new NestedSetTreeImport<MutableNode, Guid>(
                            Guid.Empty,
                            new NestedSetBranch<MutableNode>(classNode))
                    ],
                    CancellationToken.None);
    }

    /// <summary>A callback that corrupts EF's mutable hash key receives an explicit context-disposal error.</summary>
    /// <param name="scope">Whether the callback corrupts the scope instead of the node identity.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedInPlaceIdentityMutationRequiresDiscardingTheContext(
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

        var marker = new IdentityMarker { Id = 900 };
        context.Attach(marker);
        context.SavingChanges += (_, _) =>
        {
            var node = context
                .ChangeTracker
                .Entries<MutableNode>()
                .Single()
                .Entity;

            var identity = scope ? node.Scope : node.Id;
            identity.Value = "intermediate";
            context.ChangeTracker.DetectChanges();
            identity.Value = "changed";
        };

        // Act
        var result = await ImportMutableAsync(context, binary: false);

        // Assert
        var errors = Assert
            .IsType<AggregateException>(result.Error)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(errors, error => error is NestedSetException { Code: NestedSetErrorCode.InvalidImport });
        Assert.Contains(
            errors,
            error => error is InvalidOperationException
                && error.Message.Contains("Discard the context", StringComparison.Ordinal));

        Assert.True(result.Restored);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(0, await context.Set<MutableNode>().CountAsync(CancellationToken.None));
    }
}
