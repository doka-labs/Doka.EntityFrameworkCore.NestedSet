namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis;

/// <summary>Adapts existing metric properties with explicitly controlled sibling order.</summary>
internal sealed class KpiConfiguration : IEntityTypeConfiguration<Kpi>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<Kpi> builder
    )
    {
        builder.ToTable("Kpis");
        builder.HasKey(metric => metric.NodeId);

        builder
            .Property(metric => metric.NodeId)
            .ValueGeneratedNever();

        builder
            .Property(metric => metric.Title)
            .HasMaxLength(128)
            .IsRequired();

        // WHY: Explicit selectors preserve the existing domain names without requiring an interface or base entity.
        // No OrderBy is configured: presentation order is maintained by explicit hierarchy operations.
        builder.HasNestedSet(node => node
            .HasNodeKey(metric => metric.NodeId)
            .HasBounds(metric => metric.Start, metric => metric.End)
            .HasDepth(metric => metric.Level)
            .HasPosition(metric => metric.SiblingPosition)
            .HasTreeId(metric => metric.TreeId)
            .HasScope(metric => metric.ProjectId)
            .HasParent(metric => metric.ParentMetricId));

        // WHY: Including ProjectId prevents an ordinary foreign key from linking aggregates across projects.
        // Restrict keeps EF cascades from bypassing NestedSet's explicit deletion and child-promotion semantics.
        builder
            .HasOne<Kpi>()
            .WithMany()
            .HasForeignKey(metric => new
            {
                metric.ProjectId,
                metric.ParentMetricId,
            })
            .HasPrincipalKey(metric => new
            {
                metric.ProjectId,
                metric.NodeId,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
