namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Adapts instrument properties with explicitly controlled sibling order.</summary>
internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<Instrument> builder
    )
    {
        builder.ToTable("Instruments");
        builder.HasKey(instrument => instrument.NodeId);

        builder
            .Property(instrument => instrument.NodeId)
            .ValueGeneratedNever();

        builder
            .Property(instrument => instrument.Title)
            .HasMaxLength(128)
            .IsRequired();

        // WHY: Explicit selectors preserve the custom instrument property names without requiring an interface or base entity.
        // No OrderBy is configured: presentation order is maintained by explicit hierarchy operations.
        builder.HasNestedSet(node => node
            .HasNodeKey(instrument => instrument.NodeId)
            .HasBounds(instrument => instrument.Start, instrument => instrument.End)
            .HasDepth(instrument => instrument.Level)
            .HasPosition(instrument => instrument.SiblingPosition)
            .HasTreeId(instrument => instrument.TreeId)
            .HasScope(instrument => instrument.ProjectId)
            .HasParent(instrument => instrument.ParentInstrumentId));

        // WHY: Including ProjectId prevents an ordinary foreign key from linking aggregates across projects.
        // Restrict keeps EF cascades from bypassing NestedSet's explicit deletion and child-promotion semantics.
        builder
            .HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(instrument => new
            {
                instrument.ProjectId,
                instrument.ParentInstrumentId,
            })
            .HasPrincipalKey(instrument => new
            {
                instrument.ProjectId,
                instrument.NodeId,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
