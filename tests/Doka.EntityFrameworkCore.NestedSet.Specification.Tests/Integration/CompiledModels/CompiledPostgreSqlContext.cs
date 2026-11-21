namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Generates and consumes the real PostgreSql provider's compiled relational metadata.</summary>
public sealed class CompiledPostgreSqlContext : CompiledTreeContext
{
    /// <summary>Creates a design-time context without opening a database connection.</summary>
    public CompiledPostgreSqlContext() : this(
        new DbContextOptionsBuilder<CompiledPostgreSqlContext>()
            .ConfigureTestWarnings()
            .UseNpgsql("Host=localhost;Database=compiled;Username=unused", provider => provider
                .CommandTimeout(0)
                .UseNestedSets())
            .Options,
        false) { }

    /// <summary>Creates a real-provider context with a guarded generated runtime model.</summary>
    /// <param name="options">The provider connection and optional compiled model.</param>
    /// <param name="rejectModelBuilding">Whether rebuilding metadata must fail this test.</param>
    public CompiledPostgreSqlContext(
        DbContextOptions<CompiledPostgreSqlContext> options,
        bool rejectModelBuilding
    ) : base(options, rejectModelBuilding) { }
}
