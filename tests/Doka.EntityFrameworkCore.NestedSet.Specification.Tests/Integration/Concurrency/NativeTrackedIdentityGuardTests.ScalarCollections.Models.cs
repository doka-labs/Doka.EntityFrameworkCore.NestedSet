namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    public abstract partial class Scale
    {
        /// <summary>Retains exact immutable entity values across allowed and rejected explicit operations.</summary>
        private readonly record struct ScalarState(
            string Key,
            string TreeId,
            string? Parent,
            long Left,
            long Right,
            int Depth,
            long Position,
            string Name
        );

        /// <summary>Uses an explicit converted key mapping under an independently owned table identity.</summary>
        private sealed class ScalarConvertedGuardContext
            : GuardTreeContext
        {
            /// <summary>Uses the configured model and database connection.</summary>
            /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
            public ScalarConvertedGuardContext(
                DbContextOptions<ScalarConvertedGuardContext> options
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

        /// <summary>Maps native string keys with known ordinal collation and compatible nullable parents.</summary>
        private sealed class ScalarStringGuardContext
            : DbContext
        {
            /// <summary>Uses the configured model and database connection.</summary>
            /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
            public ScalarStringGuardContext(
                DbContextOptions<ScalarStringGuardContext> options
            ) : base(options) { }

            /// <inheritdoc />
            protected override void OnModelCreating(
                ModelBuilder modelBuilder
            )
            {
                var collation = Database.IsSqlite()
                    ? "BINARY"
                    : Database.IsSqlServer()
                        ? "Latin1_General_100_BIN2"
                        : Database.IsNpgsql()
                            ? "C"
                            : "utf8mb4_bin";

                var node = modelBuilder.Entity<ScalarStringNode>();
                node.ToTable("ScalarStringGuardNodes");
                node.HasKey(value => value.Id);
                node
                    .Property(value => value.Id)
                    .HasMaxLength(40)
                    .UseCollation(collation)
                    .ValueGeneratedNever();

                node
                    .Property(value => value.ParentId)
                    .HasMaxLength(40)
                    .UseCollation(collation);

                node
                    .Property(value => value.Name)
                    .HasMaxLength(100);

                node
                    .HasOne<ScalarStringNode>()
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

        /// <summary>Supplies native scalar strings independently of the converted-key control model.</summary>
        private sealed class ScalarStringNode
        {
            /// <summary>Gets or sets the exact native string key.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the stable tree identity.</summary>
            public Guid TreeId { get; set; }

            /// <summary>Gets or sets the nullable direct-parent key.</summary>
            public string? ParentId { get; set; }

            /// <summary>Gets or sets the inclusive left boundary.</summary>
            public long Left { get; set; }

            /// <summary>Gets or sets the inclusive right boundary.</summary>
            public long Right { get; set; }

            /// <summary>Gets or sets the zero-based depth.</summary>
            public int Depth { get; set; }

            /// <summary>Gets or sets the zero-based sibling position.</summary>
            public long Position { get; set; }

            /// <summary>Gets or sets the ordinary payload value.</summary>
            public string Name { get; set; } = string.Empty;
        }
    }
}
