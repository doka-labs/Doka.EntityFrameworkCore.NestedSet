namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class MutationBindingIsolationTests
{
    /// <summary>Maps one shared CLR type twice with intentionally different generic identity roles.</summary>
    private sealed class IsolationContext : DbContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        public IsolationContext(
            DbContextOptions<IsolationContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            // WHY: Distinct closed key/tree/scope roles expose cache keys based only on CLR type or caller
            // context. Both named mappings must share immutable invokers only with their exact EF metadata.
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                Alpha,
                node =>
                {
                    node.ToTable("MutationBindingAlphaNodes");
                    node
                        .IndexerProperty<int>(Id)
                        .ValueGeneratedNever();

                    node.IndexerProperty<int>(Scope);
                    node.IndexerProperty<Guid>(Tree);
                    node.IndexerProperty<int?>(Parent);
                    ConfigureCoordinates(node);
                });

            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                Beta,
                node =>
                {
                    node.ToTable("MutationBindingBetaNodes");
                    node
                        .IndexerProperty<Guid>(Id)
                        .ValueGeneratedNever();

                    node
                        .IndexerProperty<string>(Scope)
                        .HasMaxLength(40)
                        .IsRequired();

                    node
                        .IndexerProperty<string>(Tree)
                        .HasMaxLength(40)
                        .IsRequired();

                    node.IndexerProperty<Guid?>(Parent);
                    ConfigureCoordinates(node);
                });
        }
    }

    /// <summary>Reuses one shared CLR type and entity name under a different model and specialization.</summary>
    private sealed class AlternateContext : DbContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        public AlternateContext(
            DbContextOptions<AlternateContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            // WHY: A name or CLR-type cache key would select Alpha's integer specialization from the first
            // model. Exact IEntityType ownership must instead preserve this model's Guid/string/string roles.
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
                Alpha,
                node =>
                {
                    node.ToTable("MutationBindingAlternateAlphaNodes");
                    node
                        .IndexerProperty<Guid>(Id)
                        .ValueGeneratedNever();

                    node
                        .IndexerProperty<string>(Scope)
                        .HasMaxLength(40)
                        .IsRequired();

                    node
                        .IndexerProperty<string>(Tree)
                        .HasMaxLength(40)
                        .IsRequired();

                    node.IndexerProperty<Guid?>(Parent);
                    ConfigureCoordinates(node);
                });
        }
    }
}
