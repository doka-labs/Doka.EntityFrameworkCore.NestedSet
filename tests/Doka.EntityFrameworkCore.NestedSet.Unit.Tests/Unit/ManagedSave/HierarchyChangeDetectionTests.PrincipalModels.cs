namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Lets the real SQLite provider assign an inserted principal's zero-sentinel identity.</summary>
    private sealed class GeneratedPrincipalContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ProbeOwner>()
                .Property(value => value.Id)
                .ValueGeneratedOnAdd();
        }
    }

    /// <summary>Maps the guarded scope FK to an alternate key unavailable on an untracked CLR principal.</summary>
    private sealed class ShadowPrincipalContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ProbeOwner>()
                .Property<int>("ShadowId");

            modelBuilder
                .Entity<ProbeOwner>()
                .HasAlternateKey("ShadowId");

            modelBuilder
                .Entity<ProbeNode>()
                .HasOne(value => value.ScopeOwner)
                .WithMany(value => value.ScopeNodes)
                .HasForeignKey(value => value.Scope)
                .HasPrincipalKey("ShadowId");
        }
    }

    /// <summary>Uses ordinary payload as a required FK whose severing can delete a tracked hierarchy node.</summary>
    private sealed class RequiredCascadeOwnerContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ProbeNode>()
                .HasOne(value => value.Owner)
                .WithMany()
                .HasForeignKey(value => value.OwnerId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Lets an occupied owner reassignment sever another hierarchy node's required FK.</summary>
    private sealed class UniqueRequiredCascadeOwnerContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ProbeNode>()
                .HasOne(value => value.Owner)
                .WithOne()
                .HasForeignKey<ProbeNode>(value => value.OwnerId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Isolates EF's required key-only deletion path from its independent Cascade branch.</summary>
    private sealed class KeyOnlyRequiredOwnerContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            var node = modelBuilder.Entity<ProbeNode>();
            node.HasAlternateKey(value => value.OwnerId);
            node
                .HasOne(value => value.Owner)
                .WithMany()
                .HasForeignKey(value => value.OwnerId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Shares a non-key guarded criterion between two composite ordinary owner relationships.</summary>
    private sealed class NonKeyOverlapOwnerContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ProbeOwner>()
                .HasAlternateKey(value => new
                {
                    value.Payload,
                    value.Id,
                });

            var node = modelBuilder.Entity<ProbeNode>();

            // WHY: Scoped Parent keys always contain Scope. Name is a guarded, non-key ordering criterion,
            // so this fixture isolates minimal-overlap selection without hiding it behind IsKey checks.
            node
                .HasOne(value => value.Owner)
                .WithMany()
                .HasForeignKey(value => new
                {
                    value.Name,
                    value.OwnerId,
                })
                .HasPrincipalKey(value => new
                {
                    value.Payload,
                    value.Id,
                })
                .OnDelete(DeleteBehavior.Restrict);

            node.Property<int?>("SecondaryOwnerId");
            node
                .HasOne<ProbeOwner>()
                .WithMany()
                .HasForeignKey(nameof(ProbeNode.Name), "SecondaryOwnerId")
                .HasPrincipalKey(nameof(ProbeOwner.Payload), nameof(ProbeOwner.Id))
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
