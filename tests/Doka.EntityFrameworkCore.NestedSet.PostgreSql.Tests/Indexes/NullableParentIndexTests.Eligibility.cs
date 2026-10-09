using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

public sealed partial class NullableParentIndexTests
{
    /// <summary>Required columns alone do not imply partial predicates; typed tree equality does.</summary>
    /// <param name="convertedTree">Whether the Guid TreeId is converted to a text store value.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TreePredicateEligibilityRequiresQueryProof(
        bool convertedTree
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var source = database.CreateContext();
        var observer = new TreeEligibilityObserver();
        var options = new DbContextOptionsBuilder<TreeEligibilityContext>()
            .UseNpgsql(source.Database.GetConnectionString())
            .UseNestedSets()
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .AddInterceptors(observer)
            .Options;

        await using var context = new TreeEligibilityContext(options, convertedTree);
        var table = convertedTree ? "EligibilityConvertedTree" : "EligibilityNativeTree";
        var index = table + "Path";
        var storeType = convertedTree ? "text" : "uuid";
        var value = convertedTree ? "md5(((n - 1) / 20)::text)::uuid::text" : "md5(((n - 1) / 20)::text)::uuid";
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        // WHY: A scratch table with one index removes competing-cost ambiguity. The RI-shaped query must
        // prove eligibility from its own clauses even though every TreeId is physically required and non-null.
        command.CommandText = $"""
                               CREATE TABLE "{table}" ("Id" int NOT NULL, "Scope" int NOT NULL,
                                   "TreeId" {storeType} NOT NULL, "ParentId" int,
                                   "Left" bigint NOT NULL, "Right" bigint NOT NULL, "Depth" int NOT NULL, "Position" bigint NOT NULL);
                               CREATE INDEX "{index}" ON "{table}" ("Scope", "TreeId", "Left") WHERE "TreeId" IS NOT NULL;
                               INSERT INTO "{table}" SELECT n, 1, {value},
                                   CASE WHEN (n - 1) % 20 = 0 THEN NULL ELSE ((n - 1) / 20) * 20 + 1 END,
                                   CASE WHEN (n - 1) % 20 = 0 THEN 1 ELSE ((n - 1) % 20) * 2 END,
                                   CASE WHEN (n - 1) % 20 = 0 THEN 40 ELSE ((n - 1) % 20) * 2 + 1 END,
                                   CASE WHEN (n - 1) % 20 = 0 THEN 0 ELSE 1 END,
                                   CASE WHEN (n - 1) % 20 = 0 THEN 0 ELSE ((n - 1) % 20) - 1 END
                               FROM generate_series(1, 20000) seed(n);
                               """;

        await command.ExecuteNonQueryAsync(CancellationToken.None);
        var tree = Guid.Parse("cfcd2084-95d5-65ef-66e7-dff9f98764da");
        var principalQuery = $"SELECT 1 FROM ONLY \"{table}\" x "
            + "WHERE x.\"Scope\" = @scope AND x.\"Id\" = @key FOR KEY SHARE OF x";

        // Act
        var principalPlan = await ExplainAsync(context, principalQuery, true);
        var nodes = await context
            .NestedSet<TreeEligibilityNode>()
            .ForScope(1)
            .InTree(tree)
            .Nodes
            .OrderBy(node => node.Left)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        await QueryPlanTestSupport.WriteEvidenceAsync(
            "PostgreSql-tree-predicate-" + (convertedTree ? "converted" : "native"),
            ["principal\n" + principalPlan, "typed-tree\n" + observer.Plan]);

        // Assert
        Assert.Empty(IndexNames(principalPlan));
        Assert.Equal(1, ActualRows(principalPlan));
        Assert.Contains(index, IndexNames(observer.Plan!));
        Assert.Equal(20, ActualRows(observer.Plan!));
        Assert.Equal(Enumerable.Range(1, 20), nodes);
        Assert.Equal(convertedTree ? typeof(string) : typeof(Guid), observer.TreeParameterType);
    }

    /// <summary>Maps a typed query over independently created scratch index metadata.</summary>
    private sealed class TreeEligibilityContext : DbContext, ITestModelVariant
    {
        private readonly bool _convertedTree;

        internal TreeEligibilityContext(
            DbContextOptions<TreeEligibilityContext> options,
            bool convertedTree
        ) : base(options)
        {
            _convertedTree = convertedTree;
        }

        /// <inheritdoc />
        object ITestModelVariant.ModelVariant => _convertedTree;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<TreeEligibilityNode>();
            node.HasKey(entity => entity.Id);
            node
                .Property(entity => entity.Id)
                .ValueGeneratedNever();

            node.ToTable(_convertedTree ? "EligibilityConvertedTree" : "EligibilityNativeTree");

            if (_convertedTree)
            {
                node
                    .Property(entity => entity.TreeId)
                    .HasConversion<string>();
            }

            node.HasNestedSet(builder => builder
                .HasScope(entity => entity.Scope)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position));
        }
    }

    /// <summary>Retains the exact typed query's plan and already converted provider parameter.</summary>
    private sealed class TreeEligibilityObserver : DbCommandInterceptor
    {
        internal string? Plan { get; private set; }
        internal Type? TreeParameterType { get; private set; }

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            await using var explain = command.Connection!.CreateCommand();
            explain.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + command.CommandText;

            foreach (DbParameter parameter in command.Parameters)
            {
                var copy = explain.CreateParameter();
                copy.ParameterName = parameter.ParameterName;
                copy.DbType = parameter.DbType;
                copy.Value = parameter.Value;
                explain.Parameters.Add(copy);

                if (parameter.Value is Guid or string)
                {
                    TreeParameterType = parameter.Value.GetType();
                }
            }

            Plan = (string)(await explain.ExecuteScalarAsync(cancellationToken))!;

            return result;
        }
    }

    /// <summary>Retains a model Guid tree identity across either native or converted storage.</summary>
    private sealed class TreeEligibilityNode
    {
        /// <summary>Gets or sets the node key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the typed tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the ordered tree coordinate.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right coordinate.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the persisted depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the persisted sibling position.</summary>
        public long Position { get; set; }
    }
}
