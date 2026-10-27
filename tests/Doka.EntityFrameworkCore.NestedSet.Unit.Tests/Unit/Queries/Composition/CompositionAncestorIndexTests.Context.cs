namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Configures the same exact mapped roles with an explicitly named optional generated Boolean.</summary>
    private sealed class IndexCompositionContext(DbContextOptions<IndexCompositionContext> options) : DbContext(options)
    {
        /// <summary>Gets the number of ordinary fact-property evaluations performed by EF at execution.</summary>
        internal int FactGetterCalls { get; private set; }

        /// <summary>Gets or sets whether the test property throws when EF eventually evaluates it.</summary>
        internal bool ThrowFactGetter { get; set; }

        /// <summary>Gets a normal DbSet property whose application getter must not run during construction.</summary>
        internal DbSet<SelectionFact> Facts
        {
            get
            {
                FactGetterCalls++;

                if (ThrowFactGetter)
                {
                    throw new InvalidOperationException("Test fact getter must remain deferred.");
                }

                return Set<SelectionFact>();
            }
        }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<CompositionNode>();
            node.ToTable("CompositionIndexNodes");
            node.HasKey(entity => new { entity.Scope, entity.Id });
            node.Property(entity => entity.Id).ValueGeneratedNever();
            node.Property(entity => entity.Visible);
            node.HasQueryFilter(entity => entity.Visible);
            node.HasNestedSet(mapping => mapping
                .HasNodeKey(entity => entity.Id)
                .HasScope(entity => entity.Scope)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position)
                .HasAncestorIndex("HasChildren"));

            var fact = modelBuilder.Entity<SelectionFact>();
            fact.HasKey(value => value.Id);
            fact.HasQueryFilter(value => value.Visible);
            fact.HasOne(value => value.Node).WithMany()
                .HasForeignKey(value => new { value.Scope, Id = value.NodeId });
            modelBuilder.Entity<DerivedSelectionFact>().HasBaseType<SelectionFact>();

            // WHY: A second named filter can hide a hierarchy pivot after a harmless first filter.
            modelBuilder.Entity<HierarchyFilteredFact>().HasKey(value => value.Id);
            modelBuilder.Entity<HierarchyFilteredFact>().HasQueryFilter("Visible", value => value.Visible);
            modelBuilder.Entity<HierarchyFilteredFact>().HasQueryFilter("Hierarchy",
                value => Set<CompositionNode>().Any(node => node.Id == value.NodeId));
            modelBuilder.Entity<DerivedHierarchyFilteredFact>().HasBaseType<HierarchyFilteredFact>();
        }
    }
}
