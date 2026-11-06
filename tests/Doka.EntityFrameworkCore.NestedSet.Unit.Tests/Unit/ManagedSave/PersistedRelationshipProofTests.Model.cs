namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class PersistedRelationshipProofTests
{
    /// <summary>Maps the exact ordinary relationship whose write vector requires provider evidence.</summary>
    private abstract class ProofContext : DbContext
    {
        /// <summary>Keeps prepared collections and explicit property flags under caller control.</summary>
        protected ProofContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <summary>Gets whether ordinary principals receive real provider-generated identities.</summary>
        protected virtual bool GeneratedOwner => false;

        /// <summary>Gets whether the update procedure receives only the original ordinary FK value.</summary>
        protected virtual bool OriginalOnlyProcedure => false;

        /// <summary>Gets whether the procedure returns the complete single-member FK as a generated value.</summary>
        protected virtual bool ReturnedOwner => false;

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
            => optionsBuilder.UseSqlServer(
                "Server=localhost;Database=RelationshipProof;Integrated Security=True;TrustServerCertificate=True",
                provider => provider.UseNestedSets());

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var owner = modelBuilder.Entity<ProofOwner>();

            if (GeneratedOwner)
            {
                owner
                    .Property(value => value.Id)
                    .ValueGeneratedOnAdd();
            }
            else
            {
                owner
                    .Property(value => value.Id)
                    .ValueGeneratedNever();
            }

            var node = modelBuilder.Entity<ProofNode>();
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.OwnerId)
                .ValueGeneratedNever();

            node
                .Property(value => value.Name)
                .HasMaxLength(64);

            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .OrderBy(value => value.Name));

            node
                .HasOne(value => value.Owner)
                .WithMany(value => value.Nodes)
                .HasForeignKey(value => value.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            if (ReturnedOwner)
            {
                node
                    .Property(value => value.OwnerId)
                    .ValueGeneratedOnAddOrUpdate();
            }

            if (OriginalOnlyProcedure)
            {
                // WHY: EF accepts this complete procedure mapping even though a modified OwnerId has no
                // current-value input. The Modified flag alone therefore cannot prove that FK was written.
                node.UpdateUsingStoredProcedure(
                    "UpdateProofNode",
                    procedure =>
                    {
                        procedure.HasOriginalValueParameter(value => value.Id);
                        procedure.HasParameter(value => value.TreeId);
                        procedure.HasParameter(value => value.ParentId);
                        procedure.HasParameter(value => value.Left);
                        procedure.HasParameter(value => value.Right);
                        procedure.HasParameter(value => value.Depth);
                        procedure.HasParameter(value => value.Position);
                        procedure.HasParameter(value => value.Name);
                        procedure.HasOriginalValueParameter(value => value.OwnerId);

                        if (ReturnedOwner)
                        {
                            // WHY: A complete provider-returned vector is authoritative even when the procedure
                            // receives only its original FK. Merely having a result mapping is not a returned value.
                            procedure.HasResultColumn(value => value.OwnerId);
                        }
                        else
                        {
                            procedure.HasRowsAffectedReturnValue();
                        }
                    });
            }
        }
    }

    /// <summary>Uses ordinary SQL Server update metadata with no stored procedure.</summary>
    private sealed class NormalUpdateContext : ProofContext;

    /// <summary>Validates a SQL Server update procedure without any database connection or command.</summary>
    private sealed class OriginalOnlyUpdateContext : ProofContext
    {
        /// <inheritdoc />
        protected override bool OriginalOnlyProcedure => true;
    }

    /// <summary>Validates a complete FK result mapping without executing the SQL Server procedure.</summary>
    private sealed class ReturnedOwnerProcedureContext : ProofContext
    {
        /// <inheritdoc />
        protected override bool OriginalOnlyProcedure => true;

        /// <inheritdoc />
        protected override bool ReturnedOwner => true;
    }

    /// <summary>Uses SQLite's actual temporary-key generator and native FK propagation.</summary>
    private sealed class GeneratedOwnerContext : ProofContext
    {
        /// <inheritdoc />
        protected override bool GeneratedOwner => true;

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
            => optionsBuilder.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());
    }

    /// <summary>Provides an ordinary principal with an independently observable collection baseline.</summary>
    private sealed class ProofOwner
    {
        /// <summary>Gets or sets the fixed or generated principal key.</summary>
        public int Id { get; set; }

        /// <summary>Gets the prepared CLR collection that may precede EF's native membership snapshot.</summary>
        public List<ProofNode> Nodes { get; } = [];
    }

    /// <summary>Separates hierarchy ordering from one ordinary, single-member owner FK.</summary>
    private sealed class ProofNode : INestedSetNode<int, Guid>
    {
        /// <summary>Gets or sets the optional hierarchy parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the configured ordering candidate used by the unmarked-FK control.</summary>
        public string Name { get; set; } = "Before";

        /// <summary>Gets or sets an ordinary FK whose temporary value is not its own generated column.</summary>
        public int? OwnerId { get; set; }

        /// <summary>Gets or sets the ordinary principal reference independently of hierarchy structure.</summary>
        public ProofOwner? Owner { get; set; }

        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }
}
