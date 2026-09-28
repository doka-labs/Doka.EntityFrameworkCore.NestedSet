namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class KeyFilterComparisonTests
{
    /// <summary>Gets engine-neutral native key and bounded batch cases.</summary>
    public static IEnumerable<ITheoryDataRow> NativeScalarCases
    {
        get
        {
            foreach (var key in new[]
                     {
                         "Int64",
                         "Int16",
                         "Byte",
                         "Boolean",
                         "Decimal",
                         "Single",
                         "Double",
                         "DateTime",
                         "DateTimeOffset",
                         "DateOnly",
                         "TimeOnly",
                         "TimeSpan",
                     })
            {
                foreach (var count in new[] { 0, 1, 2, 64 })
                {
                    // WHY: Doka converts DateTimeOffset; this case promises an unconverted native key mapping.
                    yield return key == "DateTimeOffset"
                        ? new EngineTheoryDataRow(
                            [key, count],
                            ["MySql", "MariaDb"],
                            "Doka converts DateTimeOffset, outside this unconverted native-key mapping contract.")
                        : new TheoryDataRow<string, int>(key, count);
                }
            }
        }
    }

    /// <summary>Compares one native key type and batch shape after its isolated rows have been arranged.</summary>
    [EngineTheory]
    [EngineMemberData(nameof(NativeScalarCases))]
    public Task NativeScalarTypesPreserveCollectionEqualityAtEveryBatchSize(
        string key,
        int count
    ) => key switch
    {
        "Int64" => CompareNativeAsync(Engine, count, index => (long)int.MaxValue + index),
        "Int16" => CompareNativeAsync(Engine, count, index => (short)index),
        "Byte" => CompareNativeAsync(Engine, count, index => (byte)index),
        "Boolean" => CompareNativeAsync(Engine, count, index => index % 2 == 0),
        "Decimal" => CompareNativeAsync(Engine, count, index => index / 10m),
        "Single" => CompareNativeAsync(Engine, count, index => index / 4f),
        "Double" => CompareNativeAsync(Engine, count, index => index / 4d),
        "DateTime" => CompareNativeAsync(Engine, count, index => DateTime.UnixEpoch.AddDays(index)),
        "DateTimeOffset" => CompareNativeAsync(Engine, count, index => DateTimeOffset.UnixEpoch.AddDays(index)),
        "DateOnly" => CompareNativeAsync(Engine, count, index => DateOnly.FromDayNumber(730000 + index)),
        "TimeOnly" => CompareNativeAsync(Engine, count, index => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(index))),
        "TimeSpan" => CompareNativeAsync(Engine, count, index => TimeSpan.FromMinutes(index)),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>
    /// Runs exactly one arrange, comparison, and assertion sequence for a discovered native key case.
    /// </summary>
    private async Task CompareNativeAsync<TKey>(
        string engine,
        int count,
        Func<int, TKey> createKey
    )
        where TKey : struct
    {
        // Arrange
        var database = await _fixture.ResetAsync(engine);
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder().ConfigureTestWarnings();
        var connection = source.Database.GetConnectionString()!;

        switch (engine)
        {
            case "Sqlite":
                options.UseSqlite(connection);
                break;
            case "PostgreSql":
                options.UseNpgsql(connection);
                break;
            case "SqlServer":
                options.UseSqlServer(connection);
                break;
            default:
                options.UseMySql(
                    connection,
                    engine == "MariaDb" ? DatabaseTestTargets.MariaDb : DatabaseTestTargets.MySql);
                break;
        }

        options.UseNestedSets();
        var probe = new EnterpriseProbe(captureParameterBindings: true);
        options.AddInterceptors(probe);
        await using var context = new NativeKeyContext<TKey>(options.Options);

        // WHY: The comparison must exercise mappings accepted by the library, not only EF-mappable CLR values.
        _ = NestedSetMapping<NativeKeyNode<TKey>, TKey, int>.For(
            context,
            context.Model.FindEntityType(typeof(NativeKeyNode<TKey>))!);
        await context
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);

        try
        {
            var values = Enumerable
                .Range(0, 256)
                .Select(createKey)
                .Distinct()
                .ToArray();

            for (var index = 0; index < values.Length; index++)
            {
                await context.AddAsync(
                    new NativeKeyNode<TKey>
                    {
                        Id = values[index],
                        Scope = 1,
                        Left = (index * 2) + 1,
                        Right = (index * 2) + 2,
                        Position = index,
                    },
                    CancellationToken.None);
            }

            await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
            await ServerQueryPlanTests.RefreshStatisticsAsync<NativeKeyNode<TKey>>(context, engine);
            context.ChangeTracker.Clear();
            var evidence = new List<string>();
            var keys = values
                .Take(count)
                .ToArray();

            // Act
            var (equality, contains, production) = await CompareAsync<NativeKeyNode<TKey>, TKey>(
                context,
                probe,
                engine,
                nameof(NativeKeyNode<>.Id),
                keys,
                evidence);

            await QueryPlanTestSupport.WriteEvidenceAsync(
                $"{engine}-native-key-filter-{typeof(TKey).Name}-{count}",
                evidence);

            // Assert
            Assert.Equal(keys.Length, equality.Length);
            Assert.Equal(equality, contains);
            Assert.Equal(equality, production);
        }
        finally
        {
            // WHY: Cases reuse a fixture database but own these two model tables; cleanup makes discovery order
            // irrelevant.
            var sql = context.GetService<ISqlGenerationHelper>();

            foreach (var entity in context.Model.GetEntityTypes())
            {
                var drop = "DROP TABLE " + sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                await context.Database.ExecuteSqlRawAsync(drop, CancellationToken.None);
            }
        }
    }

    /// <summary>Maps a native scalar key without converters or provider-independent ordering assumptions.</summary>
    private sealed class NativeKeyContext<TKey> : DbContext
        where TKey : struct
    {
        /// <summary>Uses the fixture engine while retaining a distinct EF model for this CLR key type.</summary>
        internal NativeKeyContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var name = "KeyFilter_" + typeof(TKey).Name;
            var node = modelBuilder.Entity<NativeKeyNode<TKey>>();

            node.ToTable(name);
            node
                .Property(entity => entity.Id)
                .ValueGeneratedNever();
            node.HasNestedSet(builder => builder
                .HasTreeId(entity => entity.TreeId)
                .HasScope(entity => entity.Scope)
                .HasParent(entity => entity.ParentId));
        }
    }

    /// <summary>
    /// Provides actual native key and nullable-parent properties for collection translation comparisons.
    /// </summary>
    private sealed class NativeKeyNode<TKey> : IScopedNestedSetNode<TKey, Guid, int>
        where TKey : struct
    {
        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public TKey Id { get; set; }

        /// <summary>Gets or sets the isolated forest.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the optional native parent key.</summary>
        public TKey? ParentId { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }
}
