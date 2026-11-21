using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps the same CLR type independently so query guards cannot rely on the CLR type or key alone.</summary>
internal sealed class CompositionGuardContext : DbContext
{
    /// <summary>Names the first independent unscoped hierarchy mapping.</summary>
    internal const string FirstMapping = "CompositionFirst";

    /// <summary>Names the competing mapping with the same CLR type and node keys.</summary>
    internal const string SecondMapping = "CompositionSecond";

    /// <summary>Names the hierarchy mapping that requires an explicit scope binding.</summary>
    internal const string ScopedMapping = "CompositionScoped";

    /// <summary>Creates a metadata-only context with optional fail-fast I/O observers.</summary>
    internal CompositionGuardContext(
        DbContextOptions<CompositionGuardContext> options
    ) : base(options) { }

    /// <summary>Creates a SQLite model without opening a connection.</summary>
    internal static CompositionGuardContext Create(
        params IInterceptor[] interceptors
    )
    {
        var options = new DbContextOptionsBuilder<CompositionGuardContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .AddInterceptors(interceptors)
            .Options;

        return new CompositionGuardContext(options);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        Configure(modelBuilder, FirstMapping, scoped: false);
        Configure(modelBuilder, SecondMapping, scoped: false);
        Configure(modelBuilder, ScopedMapping, scoped: true);
    }

    /// <summary>Applies identical role and key configuration to independently named hierarchies.</summary>
    private static void Configure(
        ModelBuilder modelBuilder,
        string mapping,
        bool scoped
    )
    {
        modelBuilder.SharedTypeEntity<CompositionGuardNode>(
            mapping,
            entity =>
            {
                entity.ToTable(mapping);
                entity.HasKey(node => node.Id);
                entity
                    .Property(node => node.Id)
                    .ValueGeneratedNever();

                entity.HasNestedSet(hierarchy =>
                {
                    hierarchy
                        .HasNodeKey(node => node.Id)
                        .HasTreeId(node => node.TreeId)
                        .HasParent(node => node.ParentId)
                        .HasBounds(node => node.Left, node => node.Right)
                        .HasDepth(node => node.Depth)
                        .HasPosition(node => node.Position);

                    if (scoped)
                    {
                        hierarchy.HasScope(node => node.Scope);
                    }
                });
            });
    }
}
