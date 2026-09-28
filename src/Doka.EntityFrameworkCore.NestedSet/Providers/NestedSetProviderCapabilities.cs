namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Owns provider identity, transaction requirements, and lock dialect selection.</summary>
internal sealed class NestedSetProviderCapabilities
{
    /// <summary>Identifies the registered Doka provider supporting canonical table collation metadata.</summary>
    internal const string MySqlProviderName = "Doka.EntityFrameworkCore.MySql";

    /// <summary>The immutable capabilities shared by contexts using the same provider.</summary>
    private static readonly Dictionary<string, NestedSetProviderCapabilities> s_providers =
        new Dictionary<string, NestedSetProviderCapabilities>(StringComparer.Ordinal)
        {
            ["Microsoft.EntityFrameworkCore.Sqlite"] = new(NestedSetProviderKind.Sqlite),
            [MySqlProviderName] = new(NestedSetProviderKind.MySql),
            ["Npgsql.EntityFrameworkCore.PostgreSQL"] = new(NestedSetProviderKind.PostgreSql),
            ["Microsoft.EntityFrameworkCore.SqlServer"] = new(NestedSetProviderKind.SqlServer),
        };

    /// <summary>Creates immutable capabilities for one verified provider dialect.</summary>
    private NestedSetProviderCapabilities(
        NestedSetProviderKind kind
    )
    {
        Kind = kind;
    }

    /// <summary>Gets the exact supported dialect.</summary>
    internal NestedSetProviderKind Kind { get; }

    /// <summary>Gets isolation that observes the preceding writer after acquiring the hierarchy lock.</summary>
    internal IsolationLevel RequiredIsolation =>
        Kind == NestedSetProviderKind.Sqlite ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted;

    /// <summary>Gets whether deleting all referencing rows together satisfies restrictive self foreign keys.</summary>
    /// <remarks>
    /// This capability includes constraints absent from EF metadata; Doka and SQLite require unlinking.
    /// </remarks>
    internal bool SupportsStatementAtomicSelfReferentialDelete =>
        Kind is NestedSetProviderKind.PostgreSql or NestedSetProviderKind.SqlServer;

    /// <summary>Checks whether tracked keys have verified collection-parameter transport on this provider.</summary>
    /// <param name="key">The mapped primary key whose CLR values populate the parameter collection.</param>
    /// <returns>Whether the unconverted key type has passed the native collection transport matrix.</returns>
    /// <remarks>The caller must separately establish native key equality before selecting this transport.</remarks>
    internal bool SupportsTrackedKeyCollection(
        IProperty key
    )
    {
        if (key.GetTypeMapping()
                .Converter is not null)
        {
            return false;
        }

        var type = key.ClrType;

        // WHY: Integral transport was verified at native minimum/maximum values on each accepted provider.
        // Keep unsupported or converted pairs outside this transport gate even if a custom mapping permits them.
        if (type == typeof(byte)
            || type == typeof(short)
            || type == typeof(int)
            || type == typeof(long))
        {
            return true;
        }

        if (type == typeof(sbyte)
            || type == typeof(ushort)
            || type == typeof(uint))
        {
            return Kind is NestedSetProviderKind.Sqlite or NestedSetProviderKind.MySql;
        }

        return (type == typeof(ulong) && Kind == NestedSetProviderKind.MySql)
            || (type == typeof(Guid)
                && Kind is NestedSetProviderKind.Sqlite
                    or NestedSetProviderKind.MySql
                    or NestedSetProviderKind.PostgreSql
                    or NestedSetProviderKind.SqlServer);
    }

    /// <summary>Matches qualified tracked identities through native parameterized collection transport.</summary>
    /// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
    /// <typeparam name="TKey">The native primary-key type.</typeparam>
    /// <param name="source">The query already restricted to authoritative scopes and changed intervals.</param>
    /// <param name="keyProperty">The mapped primary key used for database equality.</param>
    /// <param name="uniqueKeys">Distinct captured keys that remain unchanged until enumeration completes.</param>
    /// <returns>A composable query containing only matching tracked identities.</returns>
    /// <remarks>
    ///     The caller must establish native key equality and supported collection transport first. Keys must be
    ///     unique under that equality: a rowset join repeats rows for duplicate input keys, unlike membership.
    /// </remarks>
    internal IQueryable<TEntity> MatchTrackedKeyCollection<TEntity, TKey>(
        IQueryable<TEntity> source,
        IProperty keyProperty,
        TKey[] uniqueKeys
    )
        where TEntity : class
        where TKey : notnull
    {
        var keyName = keyProperty.Name;
        if (Kind == NestedSetProviderKind.MySql)
        {
            // WHY: A genuine JSON_TABLE equality join permits indexed key lookup; Doka's membership rewrite
            // uses JSON_CONTAINS, which repeatedly scans the collection on dense affected forests.
            // AsQueryable must remain inside the selector so EF recognizes the parameter as a query root.
            return source
                .SelectMany(
                    _ => EF
                        .Parameter(uniqueKeys)
                        .AsQueryable(),
                    (
                        node,
                        key
                    ) => new
                    {
                        Node = node,
                        Key = key,
                    })
                .Where(pair => pair.Key.Equals(EF.Property<TKey>(pair.Node, keyName)))
                .Select(pair => pair.Node);
        }

        return source.Where(node => EF
            .Parameter(uniqueKeys)
            .Contains(EF.Property<TKey>(node, keyName)));
    }

    /// <summary>Resolves an exact provider name without assuming compatibility of similarly named providers.</summary>
    /// <exception cref="NotSupportedException">The provider has no verified nested-set dialect.</exception>
    internal static NestedSetProviderCapabilities Resolve(
        DbContext context
    ) => Resolve(context.Database.ProviderName);

    /// <summary>Resolves capabilities without opening a database connection.</summary>
    /// <exception cref="NotSupportedException">The provider name is null or unsupported.</exception>
    internal static NestedSetProviderCapabilities Resolve(
        string? providerName
    )
    {
        if (providerName is not null
            && s_providers.TryGetValue(providerName, out var capabilities))
        {
            return capabilities;
        }

        throw new NotSupportedException(
            "Use the SQLite, Npgsql, Doka MySQL, or Microsoft SQL Server EF Core provider.");
    }

    /// <summary>Builds an anchor read that preserves native key equality and avoids changing anchor payloads.</summary>
    internal string AnchorLockSql(
        string table,
        string column,
        string valueAlias
    )
    {
        var hint = Kind == NestedSetProviderKind.SqlServer
            ? " WITH (UPDLOCK, HOLDLOCK, ROWLOCK)"
            : string.Empty;

        // WHY: PostgreSQL FK checks acquire KEY SHARE; NO KEY UPDATE still excludes hierarchy writers while
        // allowing those checks. InnoDB instead requires a dedicated anchor because its UPDATE lock is stronger.
        var suffix = Kind switch
        {
            NestedSetProviderKind.PostgreSql => " FOR NO KEY UPDATE",
            NestedSetProviderKind.MySql => " FOR UPDATE",
            _ => string.Empty,
        };

        // WHY: SQL Server lock hints also override RCSI reads; HOLDLOCK retains protection until transaction end.
        return $"SELECT 1 AS {valueAlias} FROM {table}{hint} WHERE {column} = {{0}}{suffix}";
    }

    /// <summary>Builds first-use-safe lock creation with an atomic existing-row toggle where supported.</summary>
    internal string InfrastructureLockSql(
        string table,
        string id,
        string revision
    )
    {
        if (Kind == NestedSetProviderKind.SqlServer)
        {
            // WHY: The serializable update-range lock prevents competing first writers from inserting the same key.
            return $"INSERT INTO {table} ({id}, {revision}) SELECT {{0}}, 0 "
                + $"WHERE NOT EXISTS (SELECT 1 FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE {id} = {{0}})";
        }

        // WHY: One changing upsert both creates an absent row and holds an exclusive lock on an existing row.
        return $"INSERT INTO {table} ({id}, {revision}) VALUES ({{0}}, 0) "
            + (Kind == NestedSetProviderKind.MySql
                ? $"ON DUPLICATE KEY UPDATE {revision} = 1 - {revision}"
                : $"ON CONFLICT ({id}) DO UPDATE SET {revision} = 1 - {table}.{revision}");
    }

    /// <summary>Renders a collation using the dialect's identifier grammar without accepting SQL fragments.</summary>
    internal string CollationSql(
        ISqlGenerationHelper sql,
        string collation
    )
    {
        if (Kind != NestedSetProviderKind.SqlServer)
        {
            return sql.DelimitIdentifier(collation);
        }

        // WHY: SQL Server COLLATE requires its bare collation token; bracket-delimited identifiers are rejected.
        if (collation.Length == 0
            || collation.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException(
                "SQL Server collation names must contain only ASCII letters, digits, and underscores.");
        }

        return collation;
    }

    /// <summary>Checks provider-specific affected-row reporting for a changing infrastructure upsert.</summary>
    internal bool IsLockWriteCount(
        int affected
    ) => affected == 1 || (Kind == NestedSetProviderKind.MySql && affected == 2);

    /// <summary>Rejects SQLite types without shared ordering semantics across EF queries and raw windows.</summary>
    internal void ValidateOrdering(
        NestedSetOrdering? order
    )
    {
        if (Kind != NestedSetProviderKind.Sqlite
            || order is null)
        {
            return;
        }

        foreach (var property in order.Properties)
        {
            var providerType = property.GetTypeMapping().Converter?.ProviderClrType ?? property.ClrType;
            providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;

            // WHY: EF 10 orders decimals with EF_DECIMAL, which our raw windows do not inject.
            // The other types lack EF ordering support; both paths must agree before accepting a criterion.
            if (providerType == typeof(decimal)
                || providerType == typeof(DateTimeOffset)
                || providerType == typeof(TimeSpan)
                || providerType == typeof(ulong))
            {
                throw new InvalidOperationException(
                    $"Nested-set ordering on SQLite does not support '{property.Name}' "
                    + $"with provider type '{providerType.Name}'. "
                    + "Configure an explicit converter to a supported storage type.");
            }
        }
    }
}
