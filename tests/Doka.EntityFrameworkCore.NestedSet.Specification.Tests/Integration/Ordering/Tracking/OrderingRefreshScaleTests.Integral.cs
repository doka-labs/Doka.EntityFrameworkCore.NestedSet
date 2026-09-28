namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshScaleTests
{
    /// <summary>Provides every primitive integral key with each existing relational provider fixture.</summary>
    public static IEnumerable<TheoryDataRow<Type>> IntegralCases
    {
        get
        {
            Type[] types =
            [
                typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
                typeof(int), typeof(uint), typeof(long), typeof(ulong),
            ];
            foreach (var type in types)
            {
                yield return new TheoryDataRow<Type>(type);
            }
        }
    }

    /// <summary>
    ///     Checks native transport at integral boundaries or explicitly rejects converted structural mappings.
    /// </summary>
    [Theory]
    [MemberData(nameof(IntegralCases))]
    public async Task IntegralCollectionTransportPreservesAcceptedNativeExtremes(
        Type keyType
    )
    {
        // Arrange
        var database = await _relationalFixture.ResetAsync(Engine);
        await using var source = database.CreateContext();
        var extensions = source.GetService<IDbContextOptions>().Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptions<DbContext>(extensions);
        var method = typeof(OrderingRefreshScaleTests).GetMethod(nameof(ProbeIntegralAsync),
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        var result = await (Task<IntegralResult>)method.MakeGenericMethod(keyType).Invoke(null, [options])!;

        // Assert
        _output.WriteLine($"Engine={Engine}; key={keyType.Name}; converter={result.Converter ?? "none"}; "
            + $"input={result.Expected}; returned={result.Actual}; parameters={result.Parameters}; "
            + $"mapping failure={result.Failure?.Message ?? "none"}");
        _output.WriteLine(result.Sql);

        if (result.Converter is not null)
        {
            Assert.Null(result.Failure);
            Assert.False(result.Exact);
            Assert.Equal(0, result.Parameters);

            return;
        }

        if (result.Rejection is not null)
        {
            Assert.Contains(result.Rejection,
                Assert.IsType<InvalidOperationException>(result.Failure).Message, StringComparison.Ordinal);

            return;
        }

        Assert.Null(result.Failure);
        Assert.Equal(result.Expected, result.Actual);
        Assert.True(result.Exact);
        Assert.Equal(1, result.Parameters);

        if (Engine is "MySql" or "MariaDb")
        {
            Assert.Contains("CROSS JOIN JSON_TABLE", result.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("JSON_CONTAINS", result.Sql, StringComparison.Ordinal);
        }
    }

    /// <summary>Exercises one typed fixture model without catching provider or query failures.</summary>
    private static async Task<IntegralResult> ProbeIntegralAsync<TKey>(DbContextOptions options)
        where TKey : struct, IConvertible
    {
        using var probe = new ScaleProbe();
        await using var context = new IntegralContext<TKey>(
            new DbContextOptionsBuilder(options).ConfigureTestWarnings().AddInterceptors(probe).Options);

        var entityType = context.Model.FindEntityType(typeof(OrderingKeyNode<TKey, TKey?>))!;
        var key = entityType.FindProperty(nameof(OrderingKeyNode<TKey, TKey?>.Id))!;

        var converter = key.GetTypeMapping().Converter;
        if (converter is not null)
        {
            var convertedMap = NestedSetMapping<OrderingKeyNode<TKey, TKey?>, TKey, int>
                .For(context, entityType);

            var convertedCapabilities = NestedSetProviderCapabilities.Resolve(context);

            // WHY: EF may add a casting converter for these CLR keys. Such mappings remain valid, but the
            // native collection fast path must be excluded because its equality proof requires no converter.
            Assert.False(convertedMap.HasNativeKeyEquality);
            Assert.False(convertedCapabilities.SupportsTrackedKeyCollection(key));

            return new IntegralResult(0, 0, 0, false, converter.GetType().Name,
                null, null, "");
        }

        if (context.Database.IsSqlite() && typeof(TKey) == typeof(ulong))
        {
            // WHY: The existing SQLite ordering contract rejects UInt64 tiebreakers before any transport query.
            var failure = Record.Exception(
                () => NestedSetMapping<OrderingKeyNode<TKey, TKey?>, TKey, int>.For(context, entityType));

            return new IntegralResult(0, 0, 0, false, null, "provider type 'UInt64'", failure, "");
        }

        var map = NestedSetMapping<OrderingKeyNode<TKey, TKey?>, TKey, int>.For(context, entityType);
        var capabilities = NestedSetProviderCapabilities.Resolve(context);
        Assert.True(map.HasNativeKeyEquality);
        Assert.True(capabilities.SupportsTrackedKeyCollection(key));
        await context.GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(CancellationToken.None);
        var keys = IntegralKeys<TKey>();
        var treeId = Guid.NewGuid();
        await context.AddRangeAsync(keys.Select((keyValue, index) =>
            new OrderingKeyNode<TKey, TKey?>(keyValue, $"node-{index:D8}")
            {
                Scope = 1,
                TreeId = treeId,
                ParentId = index == 0 ? null : keys[0],
                Left = index == 0 ? 1 : index * 2,
                Right = index == 0 ? keys.Length * 2 : index * 2 + 1,
                Depth = index == 0 ? 0 : 1,
                Position = index == 0 ? 0 : index - 1,
            }), CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var actual = await capabilities.MatchTrackedKeyCollection(
                context.Set<OrderingKeyNode<TKey, TKey?>>().AsNoTracking(), key, keys)
            .Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        var command = probe.Commands.Single();

        return new IntegralResult(keys.Length, actual.Length, command.Parameters,
            keys.ToHashSet().SetEquals(actual), null, null, null, command.Sql);
    }

    /// <summary>Includes native minimum and maximum identities without decimal or floating point rounding.</summary>
    private static TKey[] IntegralKeys<TKey>()
        where TKey : struct, IConvertible
    {
        var bounds = Type.GetTypeCode(typeof(TKey)) switch
        {
            TypeCode.Byte => ((decimal)byte.MinValue, (decimal)byte.MaxValue),
            TypeCode.SByte => ((decimal)sbyte.MinValue, (decimal)sbyte.MaxValue),
            TypeCode.Int16 => ((decimal)short.MinValue, (decimal)short.MaxValue),
            TypeCode.UInt16 => ((decimal)ushort.MinValue, (decimal)ushort.MaxValue),
            TypeCode.Int32 => ((decimal)int.MinValue, (decimal)int.MaxValue),
            TypeCode.UInt32 => ((decimal)uint.MinValue, (decimal)uint.MaxValue),
            TypeCode.Int64 => ((decimal)long.MinValue, (decimal)long.MaxValue),
            TypeCode.UInt64 => ((decimal)ulong.MinValue, (decimal)ulong.MaxValue),
            _ => throw new InvalidOperationException("The test case must use a primitive integral key."),
        };

        var (minimum, maximum) = bounds;
        decimal[] edges = [minimum, minimum + 1, maximum - 1, maximum];

        return Enumerable.Range(0, 2200).Select(value => (decimal)value)
            .Where(value => value <= maximum).Concat(edges).Distinct()
            .Select(value => (TKey)Convert.ChangeType(value, typeof(TKey),
                System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    /// <summary>Uses the existing typed ordering entity with one independent native-key test table.</summary>
    private sealed class IntegralContext<TKey> : DbContext
        where TKey : struct, IConvertible
    {
        /// <summary>Reuses the existing fixture provider options without registering a new singleton service.</summary>
        internal IntegralContext(DbContextOptions options) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<OrderingKeyNode<TKey, TKey?>>();
            node.ToTable("Integral" + typeof(TKey).Name + "Nodes");
            node.Property(value => value.Id).ValueGeneratedNever();
            node.Property(value => value.Name).HasMaxLength(128);
            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId)
                .OrderBy(value => value.Name));
        }
    }

    /// <summary>
    ///     Separates a documented converter rejection from a successfully executed native collection query.
    /// </summary>
    private sealed record IntegralResult(
        int Expected,
        int Actual,
        int Parameters,
        bool Exact,
        string? Converter,
        string? Rejection,
        Exception? Failure,
        string Sql
    );
}
