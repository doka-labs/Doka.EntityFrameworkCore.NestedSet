namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Declares conflicting defaults on two owners of the same physical ancestor identity columns.</summary>
internal sealed class ConflictingTptCollationContext
    : DbContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public ConflictingTptCollationContext(
        DbContextOptions<ConflictingTptCollationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        TptInheritedCollationContext.Configure(modelBuilder);

        // WHY: NestedSet requires one effective identity comparison for the shared physical ancestor column.
        // This deliberate invalid model differs from Doka's first-owner selection and must fail before writes.
        modelBuilder
            .Entity<TptInheritedCollationNode>()
            .HasAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_unicode_ci");
    }
}
