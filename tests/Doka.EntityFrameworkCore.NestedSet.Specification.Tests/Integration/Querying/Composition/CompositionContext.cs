namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps the small neutral hierarchy and independent fact sources used by composition contracts.</summary>
internal sealed class CompositionContext : DbContext
{
    /// <summary>Creates a context connected to a fixture-owned provider database.</summary>
    internal CompositionContext(
        DbContextOptions<CompositionContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<CompositionNode>();
        node.ToTable("CompositionNodes");
        node.HasKey(entity => new
        {
            entity.Scope,
            entity.Id,
        });
        node.Property(entity => entity.Id).ValueGeneratedNever();
        node.Property(entity => entity.Visible);
        node.Property(entity => entity.EdgeOpen);
        node.Property(entity => entity.Candidate);
        node.Property(entity => entity.Matches);
        node.Property(entity => entity.FromDate);
        node.Property(entity => entity.ToDate);
        node.HasQueryFilter(entity => entity.Visible);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(entity => entity.Id)
            .HasScope(entity => entity.Scope)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));

        var firstInput = modelBuilder.Entity<CompositionInputOne>();
        firstInput.ToTable("CompositionInputsOne");
        firstInput.HasKey(entity => new
        {
            entity.Scope,
            entity.NodeId,
            entity.Date,
        });
        firstInput.Property(entity => entity.Amount);

        var secondInput = modelBuilder.Entity<CompositionInputTwo>();
        secondInput.ToTable("CompositionInputsTwo");
        secondInput.HasKey(entity => new
        {
            entity.Scope,
            entity.NodeId,
            entity.Date,
        });
        secondInput.Property(entity => entity.Amount);

        var thirdInput = modelBuilder.Entity<CompositionInputThree>();
        thirdInput.ToTable("CompositionInputsThree");
        thirdInput.HasKey(entity => new
        {
            entity.Scope,
            entity.NodeId,
            entity.Date,
        });
        thirdInput.Property(entity => entity.Amount);
    }
}
