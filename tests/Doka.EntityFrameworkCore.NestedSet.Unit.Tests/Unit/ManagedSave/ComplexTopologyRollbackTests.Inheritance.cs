namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>A derived entity restores complex storage declared on its entity base.</summary>
    /// <param name="accepted">Whether EF accepted the callback's shortened collection before rollback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InheritedDocumentRestoresDerivedEntryAfterCallback(
        bool accepted
    )
    {
        // Arrange
        await using var context = new InheritedDocumentContext();
        var document = new OptionalDocument
        {
            Title = "Stored inherited document",
            Children =
            [
                new OptionalChild { Text = "Stored inherited first" },
                new OptionalChild { Text = "Stored inherited last" },
            ],
        };

        var payload = new InheritedDocumentPayload
        {
            Id = 7,
            Document = document,
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        var mappedDocument = entry.Metadata.FindComplexProperty(nameof(InheritedDocumentBase.Document))!;
        var baseType = context.Model.FindEntityType(typeof(InheritedDocumentBase));
        var reference = entry.ComplexProperty(value => value.Document);
        var collection = reference.ComplexCollection(nameof(OptionalDocument.Children));
        var children = document.Children!;
        payload.Marker = "Pending inherited marker";
        payload.Extra = "Pending derived scalar";
        document.Title = "Pending inherited document";
        children[0].Text = "Pending inherited child";
        collection[0].Property(nameof(OptionalChild.Text)).IsModified = true;
        var marker = ObserveScalar(entry.Property(value => value.Marker));
        var extra = ObserveScalar(entry.Property(value => value.Extra));
        var title = ObserveScalar(reference.Property(nameof(OptionalDocument.Title)));
        var discriminator = entry.Metadata.FindDiscriminatorProperty()!;
        var discriminatorBefore = ObserveScalar(entry.Property(discriminator.Name));
        var referenceModified = reference.IsModified;
        var collectionModified = collection.IsModified;
        var beforeState = entry.State;
        var current = children
            .Select((child, index) => new DocumentChildObservation(child,
                collection[index].State,
                ObserveScalar(collection[index].Property(nameof(OptionalChild.Text)))))
            .ToArray();

        var originals = Enumerable
            .Range(0, children.Count)
            .Select(index => ObserveOriginal(collection.GetOriginalEntry(index)))
            .ToArray();

        // WHY: The complex property belongs to an entity base while this real tracked entry has derived
        // metadata. Resolving its declaring instance must not treat every different declaring type as complex.
        var snapshot = NestedSetTrackerSnapshot.Capture(context);
        children.RemoveAt(0);
        context.ChangeTracker.DetectChanges();

        if (accepted)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        Assert.NotNull(baseType);
        Assert.Same(baseType, mappedDocument.DeclaringType);
        Assert.NotSame(entry.Metadata, mappedDocument.DeclaringType);
        Assert.Equal(typeof(InheritedDocumentPayload), entry.Metadata.ClrType);
        Assert.Same(payload, entry.Entity);
        Assert.Equal(EntityState.Modified, beforeState);
        Assert.Equal(beforeState, entry.State);
        Assert.Same(document, payload.Document);
        Assert.Same(children, payload.Document!.Children);
        Assert.Equal(current.Length, children.Count);
        Assert.Equal(referenceModified, reference.IsModified);
        Assert.Equal(collectionModified, collection.IsModified);
        AssertScalar(entry.Property(value => value.Marker), marker);
        AssertScalar(entry.Property(value => value.Extra), extra);
        AssertScalar(reference.Property(nameof(OptionalDocument.Title)), title);
        AssertScalar(entry.Property(discriminator.Name), discriminatorBefore);
        Assert.True(discriminator.IsShadowProperty());
        Assert.Equal("Pending inherited marker", payload.Marker);
        Assert.Equal("Pending derived scalar", payload.Extra);
        Assert.Equal("Pending inherited document", document.Title);
        Assert.True(current[0].Text.Modified);
        Assert.False(current[1].Text.Modified);

        // WHY: Current and original native lanes must retain their own scalar flags after deletion/acceptance,
        // in addition to preserving the same base-declared CLR list and element references.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < current.Length; index++)
        {
            var expected = current[index];
            var element = collection[index];

            Assert.Same(expected.Instance, children[index]);
            Assert.Equal(expected.State, element.State);
            AssertScalar(element.Property(nameof(OptionalChild.Text)), expected.Text);
            Assert.Equal(expected.Text.Current, children[index].Text);
            AssertOriginal(collection.GetOriginalEntry(index), originals[index]);
        }

        Assert.False(entry.Property(value => value.Id).IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Maps real TPH inheritance with a base-declared optional JSON document and nested collection.</summary>
    private sealed class InheritedDocumentContext : DbContext
    {
        /// <summary>Keeps pending raw inherited and derived values independent of native modification flags.</summary>
        internal InheritedDocumentContext() => ChangeTracker.AutoDetectChangesEnabled = false;

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
            var root = modelBuilder.Entity<InheritedDocumentBase>();
            root.ToTable("InheritedTopologyRows");
            root.HasKey(value => value.Id);
            root
                .Property(value => value.Id)
                .ValueGeneratedNever();
            root.ComplexProperty(value => value.Document,
                document =>
                {
                    document.IsRequired(false);
                    document.ToJson("Document");
                    document.ComplexCollection(value => value.Children);
                });

            modelBuilder
                .Entity<InheritedDocumentPayload>()
                .HasBaseType<InheritedDocumentBase>();
        }
    }

    /// <summary>Declares mapped complex storage inherited by the actual tracked derived CLR entity.</summary>
    private abstract class InheritedDocumentBase
    {
        /// <summary>Gets or sets the base-declared fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets ordinary inherited application payload.</summary>
        public string Marker { get; set; } = "Stored inherited marker";

        /// <summary>Gets or sets the optional JSON graph declared on the entity base.</summary>
        public OptionalDocument? Document { get; set; }
    }

    /// <summary>Supplies distinct runtime entity metadata while inheriting the base complex-property mapping.</summary>
    private sealed class InheritedDocumentPayload : InheritedDocumentBase
    {
        /// <summary>Gets or sets ordinary derived payload independent of the inherited complex graph.</summary>
        public string Extra { get; set; } = "Stored derived scalar";
    }
}
