namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Generates and consumes the real MariaDb provider's compiled relational metadata.</summary>
public sealed class CompiledMariaDbContext : CompiledTreeContext
{
    /// <summary>Creates a design-time context without opening a database connection.</summary>
    public CompiledMariaDbContext() : this(
        new DbContextOptionsBuilder<CompiledMariaDbContext>()
            .ConfigureTestWarnings()
            .UseMySql(
                "Server=localhost;Database=compiled;User ID=unused",
                DatabaseTestTargets.MariaDb,
                provider => provider
                    .UseNestedSets())
            .Options,
        false) { }

    /// <summary>Creates a real-provider context with a guarded generated runtime model.</summary>
    /// <param name="options">The provider connection and optional compiled model.</param>
    /// <param name="rejectModelBuilding">Whether rebuilding metadata must fail this test.</param>
    public CompiledMariaDbContext(
        DbContextOptions<CompiledMariaDbContext> options,
        bool rejectModelBuilding
    ) : base(options, rejectModelBuilding) { }
}
