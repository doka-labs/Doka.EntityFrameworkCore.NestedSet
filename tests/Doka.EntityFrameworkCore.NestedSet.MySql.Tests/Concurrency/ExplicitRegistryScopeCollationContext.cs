using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Overrides inherited binary table comparison with explicit case-insensitive Scope comparison.</summary>
/// <param name="options">The provider options for this independently owned compatibility model.</param>
internal sealed class ExplicitRegistryScopeCollationContext(
    DbContextOptions<ExplicitRegistryScopeCollationContext> options
) : InheritedBinaryScopeContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        var scope = modelBuilder
            .Entity<BroadScopeNode>()
            .Property(node => node.Scope);

        scope.UseCollation("utf8mb4_general_ci");

        // WHY: The explicit column collation takes precedence over the binary physical table default.
        // EF's identity map must therefore use the same case-insensitive identity semantics for this model.
        var comparer = new ValueComparer<BroadScope>(
            (first, second) => StringComparer.OrdinalIgnoreCase.Equals(first!.Value, second!.Value),
            value => StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value),
            value => new BroadScope(value.Value));

        scope.Metadata.SetValueComparer(comparer);
    }
}
