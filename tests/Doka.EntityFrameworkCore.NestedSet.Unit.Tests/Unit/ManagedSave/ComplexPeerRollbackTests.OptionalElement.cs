namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexPeerRollbackTests
{
    /// <summary>An element with only a raw-null optional reference restores without a direct scalar fallback.</summary>
    /// <param name="originalPresent">Whether the stored baseline contains a non-null complex reference.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalOnlyElementPreservesNullAndStoredOriginalAfterPromotion(
        bool originalPresent
    )
    {
        // Arrange
        await using var context = new OptionalElementContext();
        var original = new OptionalElementValue
        {
            Text = "Stored optional text",
            Code = "Stored optional code",
            Number = 7,
        };

        var item = new OptionalOnlyElement
        {
            Value = originalPresent
                ? original
                : null,
        };

        var items = new List<OptionalOnlyElement> { item };
        var root = new OptionalElementRoot
        {
            Id = 7,
            Items = items,
        };

        var entry = context.Attach(root);
        _ = context.Model.GetRelationalModel();
        var collection = entry.ComplexCollection(value => value.Items);
        var element = collection[0];
        var reference = element.ComplexProperty(nameof(OptionalOnlyElement.Value));
        var text = reference.Property(nameof(OptionalElementValue.Text));
        var code = reference.Property(nameof(OptionalElementValue.Code));
        var number = reference.Property(nameof(OptionalElementValue.Number));
        item.Value = null;
        var beforeState = element.State;
        var textCurrent = text.CurrentValue;
        var textOriginal = text.OriginalValue;
        var codeCurrent = code.CurrentValue;
        var codeOriginal = code.OriginalValue;
        var numberCurrent = number.CurrentValue;
        var numberOriginal = number.OriginalValue;
        var collectionModified = collection.IsModified;
        var referenceModified = reference.IsModified;
        var snapshot = NestedSetTrackerSnapshot.Capture(context);

        // WHY: This element has no direct scalar or collection. Its valid optional reference is raw-null,
        // so resetting state must not depend on finding a direct scalar entry or recreating that reference.
        element.State = EntityState.Modified;
        var promotedState = element.State;
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"Optional-only element: metadata scalars={element.Metadata.ComplexType.GetProperties().Count()}; "
            + $"public property entries={element.Properties.Count()}.");

        // Act
        snapshot.Restore();

        // Assert
        Assert.Empty(element.Metadata.ComplexType.GetProperties());
        Assert.Single(element.Metadata.ComplexType.GetComplexProperties());
        Assert.Empty(element.ComplexCollections);
        Assert.Equal(EntityState.Unchanged, beforeState);
        Assert.Equal(EntityState.Modified, promotedState);
        Assert.Equal(EntityState.Unchanged, element.State);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Same(items, root.Items);
        Assert.Same(item, Assert.Single(root.Items));
        Assert.Null(item.Value);
        Assert.Null(reference.CurrentValue);
        Assert.Equal(collectionModified, collection.IsModified);
        Assert.Equal(referenceModified, reference.IsModified);
        Assert.False(element.IsModified);
        Assert.False(text.IsModified);
        Assert.False(code.IsModified);
        Assert.Equal(textCurrent, text.CurrentValue);
        Assert.Equal(
            originalPresent
                ? "Stored optional text"
                : null,
            textOriginal);

        Assert.Equal(textOriginal, text.OriginalValue);
        Assert.Equal(codeCurrent, code.CurrentValue);
        Assert.Equal(
            originalPresent
                ? "Stored optional code"
                : null,
            codeOriginal);

        Assert.Equal(codeOriginal, code.OriginalValue);
        Assert.False(number.IsModified);
        Assert.Equal(numberCurrent, number.CurrentValue);
        Assert.Equal(
            originalPresent
                ? 7
                : 0,
            numberOriginal);

        Assert.Equal(numberOriginal, number.OriginalValue);
        Assert.Equal("Stored optional text", original.Text);
        Assert.Equal("Stored optional code", original.Code);
        Assert.Equal(7, original.Number);
        Assert.False(
            entry.Property(value => value.Id)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Maps an optional-only JSON element with real EF relational metadata and no database commands.</summary>
    private sealed class OptionalElementContext : DbContext
    {
        /// <summary>Retains raw-null complex storage independently of tracked original values.</summary>
        internal OptionalElementContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
            => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var root = modelBuilder.Entity<OptionalElementRoot>();
            root
                .Property(value => value.Id)
                .ValueGeneratedNever();

            root.ComplexCollection(
                value => value.Items,
                items =>
                {
                    items.ToJson("Items");
                    items
                        .ComplexProperty(value => value.Value)
                        .IsRequired(false);
                });
        }
    }

    /// <summary>Contains a JSON collection independently of the complex element's optional-reference shape.</summary>
    private sealed class OptionalElementRoot
    {
        /// <summary>Gets or sets the fixed entity identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets ordinary entity payload unrelated to the element's scalar availability.</summary>
        public string Marker { get; set; } = "Stored marker";

        /// <summary>Gets or sets the exact list whose sole element contains only one complex reference.</summary>
        public List<OptionalOnlyElement> Items { get; set; } = [];
    }

    /// <summary>Has no direct scalar or collection, only one nullable complex reference.</summary>
    private sealed class OptionalOnlyElement
    {
        /// <summary>Gets or sets independently nullable complex storage within the JSON element.</summary>
        public OptionalElementValue? Value { get; set; }
    }

    /// <summary>Supplies a required existence marker and an independent nullable stored original.</summary>
    private sealed class OptionalElementValue
    {
        /// <summary>Gets or sets the non-null existence-determining value when the reference is present.</summary>
        public string Text { get; set; } = "";

        /// <summary>Gets or sets a nonnullable leaf with an independent stored comparison value.</summary>
        public int Number { get; set; }

        /// <summary>Gets or sets optional payload within the optional complex instance.</summary>
        public string? Code { get; set; }
    }
}
