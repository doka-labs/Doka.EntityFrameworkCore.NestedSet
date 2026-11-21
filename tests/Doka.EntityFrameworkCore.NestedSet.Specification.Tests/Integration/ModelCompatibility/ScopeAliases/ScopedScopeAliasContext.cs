namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses Scope and NodeKey as the EF key, so equal NodeKeys can exist in other scopes.</summary>
internal sealed class ScopedScopeAliasContext : DbContext
{
    /// <summary>Creates the scope-qualified identity alias context.</summary>
    internal ScopedScopeAliasContext(
        DbContextOptions<ScopedScopeAliasContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => ScopeAliasModel.Configure(
        modelBuilder,
        this,
        "ScopedScopeAliasNodes",
        "scoped_scope_alias_ci",
        node =>
        {
            node.HasKey(entity => new
            {
                entity.Scope,
                entity.Id,
            });

            node
                .HasOne<ScopeAliasNode>()
                .WithMany()
                .HasForeignKey(entity => new
                {
                    entity.Scope,
                    entity.ParentId,
                })
                .OnDelete(DeleteBehavior.Restrict);
        });
}
