namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingComplexConcurrencyTests
{
    /// <summary>Maps a computed token inside a complex value on every supported relational provider.</summary>
    private sealed class ComplexConcurrencyContext : NestedSetDbContext
    {
        /// <summary>Uses the fixture's exact provider configuration and connection.</summary>
        internal ComplexConcurrencyContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<ComplexConcurrencyNode>();
            node.ToTable("ComplexConcurrencyNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Name)
                .HasMaxLength(80);

            var sql = this.GetService<ISqlGenerationHelper>();
            var sum = string.Join(
                " + ",
                new[]
                {
                    nameof(ComplexConcurrencyNode.Left),
                    nameof(ComplexConcurrencyNode.Right),
                    nameof(ComplexConcurrencyNode.Position)
                }.Select(sql.DelimitIdentifier));

            var expression = Database.IsSqlServer() ? $"CAST({sum} AS int)" : sum;

            // WHY: A computed scalar changes with coordinate SQL on all engines and avoids trigger timing.
            node
                .ComplexProperty(value => value.Details)
                .Property(value => value.Revision)
                .HasComputedColumnSql(expression, stored: true)
                .IsConcurrencyToken();

            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId)
                .OrderBy(value => value.Name));
        }
    }

    /// <summary>Contains ordinary ordering payload and a complex generated concurrency leaf.</summary>
    private sealed class ComplexConcurrencyNode : IScopedNestedSetNode<int, Guid, int>
    {
        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int Id { get; set; }

        /// <summary>Gets or sets the forest identity.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the configured sort value.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets unrelated application payload.</summary>
        public string Payload { get; set; } = "original";

        /// <summary>Gets or sets the mapped complex concurrency container.</summary>
        public ComplexConcurrencyDetails Details { get; set; } = new();

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Stores the provider-generated concurrency token as a complex scalar.</summary>
    private sealed class ComplexConcurrencyDetails
    {
        /// <summary>Gets or sets the generated value used by later optimistic-concurrency checks.</summary>
        public int Revision { get; set; }
    }
}
