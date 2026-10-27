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
        modelBuilder.SharedTypeEntity<CompositionGuardNode>(mapping, entity =>
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

/// <summary>Provides ordinary mapped roles whose CLR identity does not identify the selected named mapping.</summary>
internal sealed class CompositionGuardNode
{
    /// <summary>Gets or sets the node key, which can have the same value in every named mapping.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the optional scope configured only on the scoped named mapping.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the direct parent key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the left structural boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the right structural boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the zero-based structural depth.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    public long Position { get; set; }
}

/// <summary>Carries selected hierarchy aliases together with independently mapped application payload.</summary>
internal sealed class CompositionGuardRow
{
    /// <summary>Gets or initializes the upper endpoint alias.</summary>
    public required CompositionGuardNode Upper { get; init; }

    /// <summary>Gets or initializes the lower endpoint alias.</summary>
    public required CompositionGuardNode Lower { get; init; }

    /// <summary>Gets or initializes payload whose mapping need not belong to the selected hierarchy.</summary>
    public CompositionGuardNode? Payload { get; init; }

    /// <summary>Gets or initializes structural distance without executing the query.</summary>
    public int Distance { get; init; }
}
