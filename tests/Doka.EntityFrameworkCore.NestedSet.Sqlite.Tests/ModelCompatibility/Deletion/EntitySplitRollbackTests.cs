using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies whole-subtree rollback when a SQLite entity-splitting fragment delete fails.</summary>
[Collection("Model compatibility")]
public sealed class EntitySplitRollbackTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the SQLite fixture owning this suite's database resources.</summary>
    /// <param name="fixture">The provider fixture owned by this suite or its test collection.</param>
    public EntitySplitRollbackTests(
        ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A later fragment failure rolls back earlier fragment deletes and parent unlinking.</summary>
    [Fact]
    public async Task EntitySplitDeleteFailureRestoresWholeSubtree()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<EntitySplitContext>(
            Engine,
            static options => new EntitySplitContext(options));

        var scenario = await MappingDeleteTestSupport.SeedAsync(
            context,
            static (id, name) => new EntitySplitNode
            {
                Id = id,
                Name = name
            },
            null);

        var sql = context.GetService<ISqlGenerationHelper>();
        var trigger = sql.DelimitIdentifier($"NestedSetRollback_{scenario.Seed}");
        var createSql = $"CREATE TRIGGER {trigger} BEFORE DELETE ON {sql.DelimitIdentifier("EntitySplitPayload")} "
            + $"WHEN OLD.{sql.DelimitIdentifier("Id")} = {scenario.Seed + 1} "
            + "BEGIN SELECT RAISE(ABORT, 'forced rollback'); END";

        await context.Database.ExecuteSqlRawAsync(createSql, CancellationToken.None);

        // Act
        Exception? error;

        try
        {
            error = await Record.ExceptionAsync(() => context
                .NestedSet<EntitySplitNode>()
                .DeleteSubtreeAsync(scenario.Seed + 1, CancellationToken.None));
        }
        finally
        {
            var dropSql = $"DROP TRIGGER {trigger}";

            await context.Database.ExecuteSqlRawAsync(dropSql, CancellationToken.None);
        }

        // Assert
        Assert.NotNull(error);
        Assert.Contains("forced rollback", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(5, await MappingDeleteTestSupport.CountRowsAsync(context, "EntitySplitPayload", scenario.Seed));
        Assert.Equal(5, await MappingDeleteTestSupport.CountRowsAsync(context, "EntitySplitStructure", scenario.Seed));
        Assert.True(
            (await context
                .NestedSet<EntitySplitNode>()
                .InTree(scenario.TargetTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }
}
