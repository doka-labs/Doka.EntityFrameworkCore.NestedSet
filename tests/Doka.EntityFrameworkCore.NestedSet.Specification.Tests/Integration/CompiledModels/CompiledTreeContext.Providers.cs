namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Generates and consumes the real MySql provider's compiled relational metadata.</summary>
public sealed class CompiledMySqlContext : CompiledTreeContext
{
    /// <summary>Creates a design-time context without opening a database connection.</summary>
    public CompiledMySqlContext() : this(
        new DbContextOptionsBuilder<CompiledMySqlContext>()
            .ConfigureTestWarnings()
            .UseMySql(
                "Server=localhost;Database=compiled;User ID=unused",
                DatabaseTestTargets.MySql,
                provider => provider
                    .UseNestedSets())
            .Options,
        false) { }

    /// <summary>Creates a real-provider context with a guarded generated runtime model.</summary>
    /// <param name="options">The provider connection and optional compiled model.</param>
    /// <param name="rejectModelBuilding">Whether rebuilding metadata must fail this test.</param>
    public CompiledMySqlContext(
        DbContextOptions<CompiledMySqlContext> options,
        bool rejectModelBuilding
    ) : base(options, rejectModelBuilding) { }
}
