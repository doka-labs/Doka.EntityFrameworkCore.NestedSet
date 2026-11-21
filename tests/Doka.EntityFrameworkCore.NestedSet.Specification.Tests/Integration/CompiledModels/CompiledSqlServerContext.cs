namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Generates and consumes the real SqlServer provider's compiled relational metadata.</summary>
public sealed class CompiledSqlServerContext : CompiledTreeContext
{
    /// <summary>Creates a design-time context without opening a database connection.</summary>
    public CompiledSqlServerContext() : this(
        new DbContextOptionsBuilder<CompiledSqlServerContext>()
            .ConfigureTestWarnings()
            .UseSqlServer(
                "Server=localhost;Database=compiled;User ID=unused;Password=unused;"
                + "TrustServerCertificate=True;MultipleActiveResultSets=False",
                provider => provider
                    .CommandTimeout(0)
                    .UseNestedSets())
            .Options,
        false) { }

    /// <summary>Creates a real-provider context with a guarded generated runtime model.</summary>
    /// <param name="options">The provider connection and optional compiled model.</param>
    /// <param name="rejectModelBuilding">Whether rebuilding metadata must fail this test.</param>
    public CompiledSqlServerContext(
        DbContextOptions<CompiledSqlServerContext> options,
        bool rejectModelBuilding
    ) : base(options, rejectModelBuilding) { }
}
