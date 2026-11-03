using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>A required key-only FK can delete its dependent without Cascade deletion behavior.</summary>
    [Fact]
    public async Task RequiredKeyOnlyOwnerClearIsHierarchyWorkWithoutCascade()
    {
        // Arrange
        await using var context = new KeyOnlyRequiredOwnerContext();
        var owner = new ProbeOwner { Id = 9 };
        var node = CreateNode();
        node.OwnerId = owner.Id;
        node.Owner = owner;
        var entry = context.Attach(node);
        node.Owner = null;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(owner.Id, node.OwnerId);
        Assert.False(entry.Property(value => value.OwnerId).IsModified);
        var foreignKey = entry.Metadata.FindNavigation(nameof(ProbeNode.Owner))!.ForeignKey;

        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(Assert
            .Single(foreignKey.Properties)
            .IsKey());
    }

    /// <summary>Shared non-key ordering fields are excluded when EF severs only ordinary FK payload.</summary>
    [Fact]
    public async Task MinimalOverlapDoesNotNullSharedNonKeyOrderingProperty()
    {
        // Arrange
        await using var context = new NonKeyOverlapOwnerContext();
        var owner = new ProbeOwner
        {
            Id = 9,
            Payload = "Alpha",
        };

        var node = CreateNode();
        node.OwnerId = owner.Id;
        node.Owner = owner;
        var entry = context.Attach(node);
        node.Owner = null;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal("Alpha", node.Name);
        Assert.Equal(owner.Id, node.OwnerId);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(entry.Property(value => value.Name).IsModified);
        Assert.False(entry.Property(value => value.OwnerId).IsModified);
        var name = entry.Metadata.FindProperty(nameof(ProbeNode.Name))!;

        Assert.False(name.IsKey());
        Assert.Equal(2,
            name
                .GetContainingForeignKeys()
                .Count());
    }

    /// <summary>Clearing ordinary required payload can delete a hierarchy node through orphan processing.</summary>
    [Fact]
    public async Task RequiredCascadeOwnerClearIsHierarchyWork()
    {
        // Arrange
        await using var context = new RequiredCascadeOwnerContext();
        var owner = new ProbeOwner
        {
            Id = 9,
            Payload = "Original owner",
        };

        var node = CreateNode();
        node.OwnerId = owner.Id;
        node.Owner = owner;
        context.Attach(node);
        var entry = context.Entry(node);
        var ownerEntry = context.Entry(owner);
        owner.Payload = "Pending owner payload";
        node.Owner = null;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Null(node.Owner);
        Assert.Equal(owner.Id, node.OwnerId);
        Assert.Equal(owner.Id,
            entry.Property(value => value.OwnerId).OriginalValue);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(entry.Property(value => value.OwnerId).IsModified);
        Assert.False(ownerEntry.Property(value => value.Payload).IsModified);
        var foreignKey = entry.Metadata.FindNavigation(nameof(ProbeNode.Owner))!.ForeignKey;

        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>Unique owner reassignment is hierarchy work only when it can orphan another hierarchy node.</summary>
    /// <param name="occupied">Whether the target owner already has another tracked hierarchy dependent.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UniqueRequiredOwnerReassignmentProtectsHierarchyVictim(
        bool occupied
    )
    {
        // Arrange
        await using var context = new UniqueRequiredCascadeOwnerContext();
        var owner = new ProbeOwner { Id = 9 };
        var victimOwner = new ProbeOwner { Id = 10 };
        var replacement = occupied ? victimOwner : new ProbeOwner { Id = 11 };
        var node = CreateNode(1);
        node.OwnerId = owner.Id;
        node.Owner = owner;
        var victim = CreateNode(2);
        victim.OwnerId = victimOwner.Id;
        victim.Owner = victimOwner;
        context.AttachRange(node, victim, replacement);
        var entry = context.Entry(node);
        var victimEntry = context.Entry(victim);
        node.Owner = replacement;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.Equal(occupied, changed);
        Assert.Same(replacement, node.Owner);
        Assert.Same(victimOwner, victim.Owner);
        Assert.Equal(owner.Id, node.OwnerId);
        Assert.Equal(victimOwner.Id, victim.OwnerId);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(EntityState.Unchanged, victimEntry.State);
        Assert.False(entry.Property(value => value.OwnerId).IsModified);
        Assert.False(victimEntry.Property(value => value.OwnerId).IsModified);
        var foreignKey = entry.Metadata.FindNavigation(nameof(ProbeNode.Owner))!.ForeignKey;

        Assert.True(foreignKey.IsUnique);
        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>An untracked generated sentinel is not evidence that the eventual principal key is equal.</summary>
    [Fact]
    public async Task GeneratedPrincipalSentinelRemainsGuarded()
    {
        // Arrange
        await using var context = new GeneratedPrincipalContext();
        var node = CreateNode();
        node.Scope = 0;
        var entry = context.Attach(node);
        var replacement = new ProbeOwner();
        node.ScopeOwner = replacement;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(0, node.Scope);
        Assert.Equal(0,
            entry.Property(value => value.Scope).OriginalValue);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(entry.Property(value => value.Scope).IsModified);
        Assert.Equal(EntityState.Detached,
            context.Entry(replacement).State);
        Assert.Single(context.ChangeTracker.Entries());
        var principalId = context.Model.FindEntityType(typeof(ProbeOwner))!.FindProperty(nameof(ProbeOwner.Id))!;

        Assert.Equal(ValueGenerated.OnAdd, principalId.ValueGenerated);
        Assert.Equal(0, principalId.Sentinel);
    }

    /// <summary>An untracked shadow principal key cannot be read as a CLR value or assumed equal.</summary>
    [Fact]
    public async Task UnknownShadowPrincipalKeyRemainsGuarded()
    {
        // Arrange
        await using var context = new ShadowPrincipalContext();
        var node = CreateNode();
        var entry = context.Attach(node);
        var replacement = new ProbeOwner { Id = node.Scope };
        node.ScopeOwner = replacement;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(changed);
        Assert.Equal(7, node.Scope);
        Assert.Equal(7,
            entry.Property(value => value.Scope).OriginalValue);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(entry.Property(value => value.Scope).IsModified);
        Assert.Equal(EntityState.Detached,
            context.Entry(replacement).State);
        Assert.Single(context.ChangeTracker.Entries());
        var foreignKey = entry.Metadata.FindNavigation(nameof(ProbeNode.ScopeOwner))!.ForeignKey;

        Assert.True(Assert
            .Single(foreignKey.PrincipalKey.Properties)
            .IsShadowProperty());
    }

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
