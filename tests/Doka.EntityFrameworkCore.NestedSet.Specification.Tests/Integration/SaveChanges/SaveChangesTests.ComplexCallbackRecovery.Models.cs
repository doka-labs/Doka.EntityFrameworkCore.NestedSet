namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Combines the hierarchy with an ordinary, independently versioned complex payload row.</summary>
    private sealed class ComplexCallbackContext(DbContextOptions options) : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigureNavigationModel(modelBuilder);
            var row = modelBuilder.Entity<ComplexCallbackRow>();
            row.ToTable("ComplexCallbackRows");
            row
                .Property(value => value.Id)
                .ValueGeneratedNever();

            row
                .Property(value => value.Label)
                .HasMaxLength(128);

            row
                .Property(value => value.Version)
                .IsConcurrencyToken();

            row.ComplexCollection(
                value => value.Items,
                items =>
                {
                    items.ToJson("Items");
                    items.IsRequired(false);
                    items
                        .Property(value => value.Text)
                        .IsRequired();

                    items.ComplexCollection(
                        value => value.Parts,
                        parts =>
                        {
                            parts.IsRequired(false);
                            parts
                                .Property(value => value.Text)
                                .IsRequired();
                        });
                });

            row.ComplexProperty(
                value => value.Address,
                address =>
                {
                    address.ToJson("Address");
                    address.IsRequired(false);
                    address
                        .Property(value => value.Text)
                        .IsRequired();

                    address.ComplexCollection(
                        value => value.Tags,
                        tags =>
                        {
                            tags.IsRequired(false);
                            tags
                                .Property(value => value.Text)
                                .IsRequired();
                        });
                });
        }
    }

    /// <summary>Contains ordinary application data without table sharing or hierarchy-owned properties.</summary>
    private sealed class ComplexCallbackRow
    {
        /// <summary>Gets or sets the assigned application identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets explicit ordinary payload used by managed callbacks and concurrency tests.</summary>
        public string Label { get; set; } = "original row";

        /// <summary>Gets or sets the independent application-managed concurrency token.</summary>
        public int Version { get; set; } = 7;

        /// <summary>Gets or sets an optional JSON complex collection with nested collections.</summary>
        public List<ComplexCallbackItem>? Items { get; set; } = [];

        /// <summary>Gets or sets an optional JSON complex reference containing another complex collection.</summary>
        public ComplexCallbackAddress? Address { get; set; }
    }

    /// <summary>Provides a reference collection element with independently mutable nested complex values.</summary>
    private sealed class ComplexCallbackItem
    {
        /// <summary>Gets or sets the element's ordinary scalar payload.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Gets or sets an optional nested complex collection.</summary>
        public List<ComplexCallbackLeaf>? Parts { get; set; } = [];
    }

    /// <summary>Provides an optional complex reference and its nested collection.</summary>
    private sealed class ComplexCallbackAddress
    {
        /// <summary>Gets or sets the required presence leaf.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Gets or sets optional nested complex collection values.</summary>
        public List<ComplexCallbackLeaf>? Tags { get; set; } = [];
    }

    /// <summary>Provides a nested reference element whose identity and scalar state must survive recovery.</summary>
    private sealed class ComplexCallbackLeaf
    {
        /// <summary>Gets or sets the nested element's ordinary scalar payload.</summary>
        public string Text { get; set; } = string.Empty;
    }
}
