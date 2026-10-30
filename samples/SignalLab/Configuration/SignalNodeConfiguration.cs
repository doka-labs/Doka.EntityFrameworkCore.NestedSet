namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Maps reporting payload and shadow hierarchy roles through an ordinary EF entity configuration.</summary>
internal sealed class SignalNodeConfiguration : IEntityTypeConfiguration<SignalNode>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<SignalNode> builder
    )
    {
        builder.ToTable("ReportingNodes");
        builder.HasKey(value => new { value.TenantId, value.Id });
        builder.HasAlternateKey(value => new { value.TenantId, value.TypeId, value.Id });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Name).HasMaxLength(128).IsRequired();
        builder.Property<Guid>(SignalStorage.TreeId);
        builder.Property<Guid?>(SignalStorage.ParentId);
        builder.Property<long>(SignalStorage.Left);
        builder.Property<long>(SignalStorage.Right);
        builder.Property<int>(SignalStorage.Depth);
        builder.Property<long>(SignalStorage.Position);
        builder.HasNestedSet(mapping => mapping
            .HasNodeKey(value => value.Id)
            .HasScope(value => value.TenantId)
            .HasTreeId(SignalStorage.TreeId)
            .HasParent(SignalStorage.ParentId)
            .HasBounds(SignalStorage.Left, SignalStorage.Right)
            .HasDepth(SignalStorage.Depth)
            .HasPosition(SignalStorage.Position)
            .OrderBy(value => value.Name));

        // WHY: Tenant participates in the direct-parent FK, and Restrict prevents EF cascades bypassing hierarchy
        // operations.
        builder.HasOne<SignalNode>().WithMany()
            .HasForeignKey(nameof(SignalNode.TenantId), SignalStorage.ParentId)
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter("Visibility", value => value.IsVisible);

        builder.HasOne<NodeKind>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.TypeId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
