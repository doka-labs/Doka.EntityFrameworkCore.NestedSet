namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses NodeKey alone as the EF key, so refresh needs no scope correlation.</summary>
internal sealed class ScalarScopeAliasContext : DbContext
{
    /// <summary>Creates the scalar-identity alias context.</summary>
    internal ScalarScopeAliasContext(
        DbContextOptions<ScalarScopeAliasContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => ScopeAliasModel.Configure(
        modelBuilder,
        this,
        "ScalarScopeAliasNodes",
        "scalar_scope_alias_ci",
        node =>
        {
            node.HasKey(entity => entity.Id);
            node
                .HasOne<ScopeAliasNode>()
                .WithMany()
                .HasPrincipalKey(entity => new
                {
                    entity.Scope,
                    entity.Id,
                })
                .HasForeignKey(entity => new
                {
                    entity.Scope,
                    entity.ParentId,
                })
                .OnDelete(DeleteBehavior.Restrict);
        });
}
