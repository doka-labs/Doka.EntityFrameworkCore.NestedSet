namespace Doka.EntityFrameworkCore.NestedSet.Postgres;

internal sealed partial class PostgresNestedSetProvider
{
    /// <inheritdoc />
    internal override bool UsesUnicodeScalarLength(
        RelationalTypeMapping mapping
    )
    {
        var type = mapping.GetType();

        // WHY: Npgsql's mapping implementations are internal API. Exact ownership plus the two concrete
        // identities accepts their runtime clones without trusting subclasses, foreign assemblies or store names.
        if (!ReferenceEquals(type.Assembly, ProviderAssembly)
            || type.FullName is not (
                "Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping.NpgsqlStringTypeMapping"
                or "Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping.NpgsqlCharacterStringTypeMapping"))
        {
            return false;
        }

        var storeType = mapping.StoreTypeNameBase;

        return storeType.Equals("character varying", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("varchar", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("character", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("char", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("bpchar", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    internal override bool StoresAncestorFlag => true;

    /// <inheritdoc />
    internal override void AddModelFinalizers(
        ConventionSet conventions,
        ISqlGenerationHelper sql
    ) =>
        // WHY: EF drains self-FK conventions after each finalizer. The dependent index exists only after
        // the preceding common hierarchy finalizer finishes its relationship-convention batch.
        conventions.ModelFinalizingConventions.Add(new PostgresParentIndexes(sql));

    /// <inheritdoc />
    internal override bool IsCompatibleIndexAnnotation(
        IConventionAnnotation annotation
    ) => annotation.Name switch
    {
        "Npgsql:IndexMethod" => annotation.Value is "btree",
        "Npgsql:IndexInclude" => annotation.Value is string[],
        "Npgsql:CreatedConcurrently" => annotation.Value is bool,
        "Npgsql:StorageParameter:fillfactor" => annotation.Value is int and >= 10 and <= 100,
        _ => false,
    };
}
