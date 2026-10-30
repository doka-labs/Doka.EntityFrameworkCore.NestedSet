namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Preserves representable signed bounds when the optional generated access path is introduced.</summary>
[DatabasePlatform]
[Collection(AncestorIndexFixtureRegistration.Name)]
public sealed class AncestorIndexBoundaryTests
{
    private readonly MigrationFixture _fixture;

    /// <summary>Creates boundary scenarios using isolated databases on the shared native engines.</summary>
    /// <param name="fixture">The owner of the lazily started database engines.</param>
    public AncestorIndexBoundaryTests(
        MigrationFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Gets signed-boundary cases and nearby positive and negative controls for every engine.</summary>
    public static TheoryData<string, long, long, bool> SignedBounds
    {
        get
        {
            var cases = new TheoryData<string, long, long, bool>();
            foreach (var engine in new[] { "Sqlite", "MySql", "MariaDb", "PostgreSql", "SqlServer" })
            {
                cases.Add(engine, long.MaxValue, long.MaxValue, false);
                cases.Add(engine, long.MaxValue, long.MinValue, false);
                cases.Add(engine, long.MaxValue - 1, long.MaxValue, false);
                cases.Add(engine, long.MaxValue - 2, long.MaxValue, true);
                cases.Add(engine, long.MinValue, long.MaxValue, true);
            }

            return cases;
        }
    }

    /// <summary>Computes the flag without narrowing the storage domain of the signed boundary columns.</summary>
    /// <param name="engine">The real relational engine.</param>
    /// <param name="left">The persisted signed left boundary.</param>
    /// <param name="right">The persisted signed right boundary.</param>
    /// <param name="expected">Whether the interval has room for another boundary.</param>
    [Theory]
    [MemberData(nameof(SignedBounds))]
    public async Task GeneratedFlagPreservesSignedBoundsOnInsert(
        string engine,
        long left,
        long right,
        bool expected
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(_fixture, engine);
        var chain = database.Scaffold(true, "AncestorSignedInitial", splitStructure: true);
        await database.MigrateAsync(chain, splitStructure: true);
        await using var context = database.CreateContext(assembly: chain.Assembly, splitStructure: true);

        // WHY: A raw legacy/imported interval can be empty or malformed until repaired. Adding an optional
        // read access path must not introduce arithmetic failure before validation can inspect that row.
        // Act
        await InsertSplitRowAsync(context, database.PhysicalDatabase.Schema, left, right);

        // Assert
        var actual = await context
            .Set<MigrationNode>()
            .Select(x => new
            {
                x.Left,
                x.Right,
                Flag = EF.Property<bool>(x, AncestorIndexContext.FlagProperty)
            })
            .SingleAsync(CancellationToken.None);

        Assert.Equal(left, actual.Left);
        Assert.Equal(right, actual.Right);
        Assert.Equal(expected, actual.Flag);
    }

    /// <summary>Adds the generated access path without rejecting rows already representable by the schema.</summary>
    /// <param name="engine">The real relational engine.</param>
    /// <param name="left">The previously persisted signed left boundary.</param>
    /// <param name="right">The previously persisted signed right boundary.</param>
    /// <param name="expected">Whether the interval has room for another boundary.</param>
    [Theory]
    [MemberData(nameof(SignedBounds))]
    public async Task PopulatedUpgradePreservesSignedBounds(
        string engine,
        long left,
        long right,
        bool expected
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(_fixture, engine);
        var initial = database.Scaffold(false, "AOrdinarySignedInitial", splitStructure: true);
        await database.MigrateAsync(initial, false, splitStructure: true);
        await using (var ordinary = database.CreateContext(false, initial.Assembly, splitStructure: true))
        {
            await InsertSplitRowAsync(ordinary, database.PhysicalDatabase.Schema, left, right);
        }

        var upgrade = database.Scaffold(true, "BAncestorSignedUpgrade", initial, splitStructure: true);

        // Act
        await database.MigrateAsync(upgrade, splitStructure: true);

        // Assert
        await using var context = database.CreateContext(assembly: upgrade.Assembly, splitStructure: true);
        var actual = await context
            .Set<MigrationNode>()
            .Select(x => new
            {
                x.Left,
                x.Right,
                Flag = EF.Property<bool>(x, AncestorIndexContext.FlagProperty)
            })
            .SingleAsync(CancellationToken.None);

        Assert.Equal(left, actual.Left);
        Assert.Equal(right, actual.Right);
        Assert.Equal(expected, actual.Flag);
    }

    /// <summary>Seeds one legacy row atomically across the real primary and secondary mapped tables.</summary>
    private static async Task InsertSplitRowAsync(
        AncestorIndexContext context,
        string? schema,
        long left,
        long right
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var primary = sql.DelimitIdentifier(MigrationContext.TableName, schema);
        var structure = sql.DelimitIdentifier(MigrationContext.TableName + "_structure", schema);
        var key = sql.DelimitIdentifier("entry_id");
        var payload = sql.DelimitIdentifier("entry_payload");
        var category = sql.DelimitIdentifier("entry_category");
        var scope = sql.DelimitIdentifier("tree_scope");
        var tree = sql.DelimitIdentifier("tree_id");
        var leftColumn = sql.DelimitIdentifier("left_bound");
        var rightColumn = sql.DelimitIdentifier("right_bound");
        var depth = sql.DelimitIdentifier("entry_depth");
        var position = sql.DelimitIdentifier("sibling_position");
        await using var transaction = await context.Database.BeginTransactionAsync(CancellationToken.None);

        // WHY: The schema permits repairable malformed split intervals. Native parameterized inserts
        // represent an existing/imported database without bypassing production validation through internals.
        // Identifiers are fixed mapped test names and always delimited by the active provider.
        await ExecuteInsertAsync(
            context,
            transaction.GetDbTransaction(),
            $"INSERT INTO {primary} ({key}, {payload}, {category}) VALUES (@value0, @value1, @value2)",
            [61, string.Empty, 0]);

        await ExecuteInsertAsync(
            context,
            transaction.GetDbTransaction(),
            $"INSERT INTO {structure} ({key}, {scope}, {tree}, {leftColumn}, {rightColumn}, {depth}, {position}) "
            + "VALUES (@value0, @value1, @value2, @value3, @value4, @value5, @value6)",
            [61, 7, 11, left, right, 0, 0]);

        await transaction.CommitAsync(CancellationToken.None);
    }

    /// <summary>Executes native DML with provider-created typed parameters and the caller's transaction.</summary>
    private static async Task ExecuteInsertAsync(
        AncestorIndexContext context,
        DbTransaction transaction,
        string sql,
        object[] values
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;
        for (var index = 0; index < values.Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@value" + index;
            parameter.Value = values[index];
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
