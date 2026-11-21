namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares the case-insensitive scope mapping between both identity forms.</summary>
internal static class ScopeAliasModel
{
    /// <summary>Maps the table, native case-insensitive scope collation, identity, and nested-set roles.</summary>
    internal static void Configure(
        ModelBuilder modelBuilder,
        DbContext context,
        string table,
        string postgresCollation,
        Action<EntityTypeBuilder<ScopeAliasNode>> identity
    )
    {
        if (context.Database.IsNpgsql())
        {
            modelBuilder.HasCollation(
                postgresCollation,
                locale: "und-u-ks-level2",
                provider: "icu",
                deterministic: false);
        }

        var collation = context.Database.IsSqlite()
            ? "NOCASE"
            : context.Database.IsNpgsql()
                ? postgresCollation
                : context.Database.IsSqlServer()
                    ? "Latin1_General_100_CI_AS"
                    : "utf8mb4_general_ci";

        var node = modelBuilder.Entity<ScopeAliasNode>();
        node.ToTable(table);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node
            .Property(entity => entity.Scope)
            .HasMaxLength(40)
            .UseCollation(collation);

        node
            .Property(entity => entity.Name)
            .HasMaxLength(100);

        identity(node);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasScope(entity => entity.Scope)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position)
            .OrderBy(entity => entity.Name));
    }
}
