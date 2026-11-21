namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Maps a supported optional JSON complex reference containing a class-based complex collection.</summary>
    private sealed class OptionalCollectionContext : DbContext
    {
        /// <summary>Retains the distinction between raw CLR state and explicitly detected state.</summary>
        internal OptionalCollectionContext()
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
            var payload = modelBuilder.Entity<OptionalCollectionPayload>();
            payload
                .Property(value => value.Id)
                .ValueGeneratedNever();

            payload.ComplexProperty(
                value => value.Document,
                document =>
                {
                    document.IsRequired(false);
                    document.ToJson("Document");
                    document.ComplexCollection(value => value.Children);
                });
        }
    }

    /// <summary>Contains independent scalar payload and optional JSON graph storage.</summary>
    private sealed class OptionalCollectionPayload
    {
        /// <summary>Gets or sets the fixed tracked identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets payload whose raw value and original are independent of the document.</summary>
        public string Marker { get; set; } = "Stored optional marker";

        /// <summary>Gets or sets the optional complex container holding the child collection.</summary>
        public OptionalDocument? Document { get; set; }
    }

    /// <summary>Provides a required existence marker and independently nullable nested list.</summary>
    private sealed class OptionalDocument
    {
        /// <summary>Gets or sets the existence-determining scalar within the optional reference.</summary>
        public string Title { get; set; } = "";

        /// <summary>Gets or sets the exact child list retained across recovery.</summary>
        public List<OptionalChild>? Children { get; set; }
    }

    /// <summary>Provides one observable nested complex scalar and native element state.</summary>
    private sealed class OptionalChild
    {
        /// <summary>Gets or sets child payload with an independent original buffer.</summary>
        public string Text { get; set; } = "";
    }

    /// <summary>Retains optional topology separately from installed native slots beneath a null CLR path.</summary>
    private sealed record DocumentObservation(
        EntityState State,
        ScalarObservation Marker,
        OptionalDocument? Instance,
        ScalarObservation Title,
        bool ReferenceModified,
        List<OptionalChild>? Children,
        bool CollectionModified,
        DocumentChildObservation[] Current,
        EntityState[] InstalledStates,
        OriginalChildObservation[] Originals
    );

    /// <summary>Retains one visible current child separately from its scalar original and modification flag.</summary>
    private sealed record DocumentChildObservation(
        OptionalChild Instance,
        EntityState State,
        ScalarObservation Text
    );

    /// <summary>Retains native original-only state without reading an unavailable current CLR value.</summary>
    private sealed record OriginalChildObservation(
        EntityState State,
        object? Value,
        bool Modified
    );
}
