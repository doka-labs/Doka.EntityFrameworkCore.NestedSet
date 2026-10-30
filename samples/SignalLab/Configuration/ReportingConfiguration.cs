using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Maps reporting domain constraints separately from the library's structural conventions.</summary>
internal static class ReportingConfiguration
{
    /// <summary>Applies the same tenant, classification and comparison contracts to every qualified provider.</summary>
    internal static void Configure(
        ModelBuilder model,
        SignalLabContext context
    )
    {
        var type = model.Entity<NodeKind>();
        type.ToTable("NodeKinds");
        type.HasKey(value => new { value.TenantId, value.Id });
        type.Property(value => value.Id).ValueGeneratedNever();
        type.Property(value => value.Code).HasMaxLength(64).IsRequired();

        model.ApplyConfiguration(new SignalNodeConfiguration());

        var definition = model.Entity<TagDefinition>();
        definition.ToTable("TagDefinitions");
        definition.HasKey(value => new { value.TenantId, value.TypeId, value.FieldCode });
        definition.Property(value => value.FieldCode).HasMaxLength(64);
        definition.Property(value => value.Label).HasMaxLength(128);
        definition.HasOne<NodeKind>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.TypeId })
            .OnDelete(DeleteBehavior.Restrict);

        var choice = model.Entity<TagChoice>();
        choice.ToTable("TagChoices");
        choice.HasKey(value => new { value.TenantId, value.TypeId, value.FieldCode, value.Value });
        choice.Property(value => value.FieldCode).HasMaxLength(64);
        choice.Property(value => value.Value).HasMaxLength(128);
        choice.HasOne<TagDefinition>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.TypeId, value.FieldCode })
            .OnDelete(DeleteBehavior.Restrict);

        var metadata = model.Entity<TagValue>();
        metadata.ToTable("TagValues");
        metadata.HasKey(value => new { value.TenantId, value.NodeId, value.FieldCode });
        metadata.Property(value => value.FieldCode).HasMaxLength(64);
        metadata.Property(value => value.Value).HasMaxLength(128);
        metadata.HasOne<TagDefinition>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.TypeId, value.FieldCode })
            .OnDelete(DeleteBehavior.Restrict);

        metadata.HasOne<SignalNode>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.TypeId, value.NodeId })
            .HasPrincipalKey(value => new { value.TenantId, value.TypeId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);

        metadata.HasIndex(value => new { value.TenantId, value.TypeId, value.FieldCode, value.Value, value.NodeId });

        // WHY: Binary collations preserve the fixed codes' case. The report predicate separately preserves
        // trailing spaces because MySQL and SQL Server pad ordinary string comparisons.
        var collation = context.Database.ProviderName switch
        {
            "Doka.EntityFrameworkCore.MySql" => "utf8mb4_bin",
            "Microsoft.EntityFrameworkCore.SqlServer" => "Latin1_General_100_BIN2",
            "Microsoft.EntityFrameworkCore.Sqlite" => "BINARY",
            _ => "C",
        };

        definition.Property(value => value.FieldCode).UseCollation(collation);
        choice.Property(value => value.FieldCode).UseCollation(collation);
        choice.Property(value => value.Value).UseCollation(collation);
        metadata.Property(value => value.FieldCode).UseCollation(collation);
        metadata.Property(value => value.Value).UseCollation(collation);

        var period = model.Entity<LinkWindow>();
        var sql = context.GetService<ISqlGenerationHelper>();
        var from = sql.DelimitIdentifier(nameof(LinkWindow.FromDate));
        var to = sql.DelimitIdentifier(nameof(LinkWindow.ToDate));
        period.ToTable("LinkAvailability", table => table.HasCheckConstraint(
            "CK_LinkAvailability_OrderedDays", $"{to} IS NULL OR {to} >= {from}"));

        period.HasKey(value => new { value.TenantId, value.ParentId, value.ChildId, value.FromDate });
        period.HasOne<SignalNode>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ParentId })
            .OnDelete(DeleteBehavior.Restrict);

        period.HasOne<SignalNode>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ChildId })
            .OnDelete(DeleteBehavior.Restrict);

        var channel = model.Entity<SignalChannel>();
        channel.ToTable("SignalChannels");
        channel.HasKey(value => new { value.TenantId, value.Id });
        channel.Property(value => value.Id).ValueGeneratedNever();
        channel.Property(value => value.Name).HasMaxLength(128);
        channel.Property(value => value.UnitCode).HasMaxLength(32);
        channel.HasOne<NodeKind>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.MeasurementTypeId })
            .OnDelete(DeleteBehavior.Restrict);

        ConfigurePulse(model.Entity<PulseRecord>());
        ConfigureSweep(model.Entity<SweepRecord>());
        ConfigureBurst(model.Entity<BurstRecord>());
    }

    /// <summary>Maps the pulse port's event identity and native counter fields.</summary>
    private static void ConfigurePulse(EntityTypeBuilder<PulseRecord> input)
    {
        input.ToTable("PulseRecords");
        input.HasKey(value => value.Sequence);
        input.Property(value => value.Sequence).ValueGeneratedOnAdd();

        // WHY: Equal-valued events at the same node/time are distinct inputs. The lookup index is
        // deliberately nonunique; using the analytical grain as a key would silently prohibit them.
        input.HasIndex(value => new { value.TenantId, value.ChannelId, value.NodeId, value.Granularity, value.Day });
        input.HasOne<SignalChannel>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ChannelId })
            .OnDelete(DeleteBehavior.Restrict);

        input.HasOne<SignalNode>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.NodeId })
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>Maps receiver integration windows without pretending they share the pulse format.</summary>
    private static void ConfigureSweep(EntityTypeBuilder<SweepRecord> input)
    {
        input.ToTable("SweepRecords");
        input.HasKey(value => value.Sequence);
        input.Property(value => value.Sequence).ValueGeneratedOnAdd();
        input.HasIndex(value => new { value.TenantId, value.StreamId, value.ReceiverId, value.Resolution, value.CompletedOn });
        input.HasOne<SignalChannel>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.StreamId })
            .OnDelete(DeleteBehavior.Restrict);

        input.HasOne<SignalNode>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ReceiverId })
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>Maps device bursts using their own physical field names and event identity.</summary>
    private static void ConfigureBurst(EntityTypeBuilder<BurstRecord> input)
    {
        input.ToTable("BurstRecords");
        input.HasKey(value => value.Sequence);
        input.Property(value => value.Sequence).ValueGeneratedOnAdd();
        input.HasIndex(value => new { value.TenantId, value.SignalId, value.DeviceId, value.BucketSize, value.CapturedOn });
        input.HasOne<SignalChannel>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.SignalId })
            .OnDelete(DeleteBehavior.Restrict);

        input.HasOne<SignalNode>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
