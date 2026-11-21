namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Maps native Guid keys independently of the converted TreeId control.</summary>
        private sealed class NativeGuidGuardContext
            : DbContext
        {
            /// <summary>Uses the configured model and database connection.</summary>
            /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
            public NativeGuidGuardContext(
                DbContextOptions<NativeGuidGuardContext> options
            ) : base(options) { }

            /// <inheritdoc />
            protected override void OnModelCreating(
                ModelBuilder modelBuilder
            )
            {
                var node = modelBuilder.Entity<NativeGuidGuardNode>();
                node.ToTable("NativeGuidGuardNodes");
                node.HasKey(value => value.Id);
                node
                    .Property(value => value.Id)
                    .ValueGeneratedNever();

                node
                    .Property(value => value.Name)
                    .HasMaxLength(100);

                node
                    .HasOne<NativeGuidGuardNode>()
                    .WithMany()
                    .HasForeignKey(value => value.ParentId)
                    .OnDelete(DeleteBehavior.Restrict);

                node.HasNestedSet(nestedSet => nestedSet
                    .HasNodeKey(value => value.Id)
                    .HasTreeId(value => value.TreeId)
                    .HasParent(value => value.ParentId)
                    .HasBounds(value => value.Left, value => value.Right)
                    .HasDepth(value => value.Depth)
                    .HasPosition(value => value.Position));
            }
        }

        /// <summary>Stores converted NodeKeys to exercise the bounded scalar fallback.</summary>
        private sealed class ConvertedKeyGuardContext
            : GuardTreeContext
        {
            /// <summary>Uses the configured model and database connection.</summary>
            /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
            public ConvertedKeyGuardContext(
                DbContextOptions<ConvertedKeyGuardContext> options
            ) : base(options) { }

            /// <inheritdoc />
            protected override bool CaseInsensitive => true;

            /// <inheritdoc />
            protected override bool BroadComparer => false;

            /// <inheritdoc />
            protected override void OnModelCreating(
                ModelBuilder modelBuilder
            )
            {
                base.OnModelCreating(modelBuilder);
                var node = modelBuilder.Entity<BroadTreeIdNode>();
                node
                    .Property(value => value.Id)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                node
                    .Property(value => value.ParentId)
                    .HasConversion<string>()
                    .HasMaxLength(20);
            }
        }

        /// <summary>Provides exact native Guid identities and ordinary mutable hierarchy metadata.</summary>
        private sealed class NativeGuidGuardNode
        {
            /// <summary>Gets or sets the native node key.</summary>
            public Guid Id { get; set; }

            /// <summary>Gets or sets the stable tree identity.</summary>
            public Guid TreeId { get; set; }

            /// <summary>Gets or sets the immediate parent key.</summary>
            public Guid? ParentId { get; set; }

            /// <summary>Gets or sets the left traversal bound.</summary>
            public long Left { get; set; }

            /// <summary>Gets or sets the right traversal bound.</summary>
            public long Right { get; set; }

            /// <summary>Gets or sets the root-relative depth.</summary>
            public int Depth { get; set; }

            /// <summary>Gets or sets the sibling position.</summary>
            public long Position { get; set; }

            /// <summary>Gets or sets the ordinary application payload.</summary>
            public string Name { get; set; } = string.Empty;
        }
    }
}
