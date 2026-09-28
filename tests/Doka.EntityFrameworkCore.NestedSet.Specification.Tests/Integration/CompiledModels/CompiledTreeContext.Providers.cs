namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Generates and consumes the real MySql provider's compiled relational metadata.</summary>
public sealed class CompiledMySqlContext : CompiledTreeContext
{
    /// <summary>Creates a design-time context without opening a database connection.</summary>
    public CompiledMySqlContext() : this(
        new DbContextOptionsBuilder<CompiledMySqlContext>()
            .ConfigureTestWarnings()
            .UseMySql("Server=localhost;Database=compiled;User ID=unused", DatabaseTestTargets.MySql)
            .UseNestedSets()
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

/// <summary>Generates and consumes the real MariaDb provider's compiled relational metadata.</summary>
public sealed class CompiledMariaDbContext : CompiledTreeContext
{
    /// <summary>Creates a design-time context without opening a database connection.</summary>
    public CompiledMariaDbContext() : this(
        new DbContextOptionsBuilder<CompiledMariaDbContext>()
            .ConfigureTestWarnings()
            .UseMySql("Server=localhost;Database=compiled;User ID=unused", DatabaseTestTargets.MariaDb)
            .UseNestedSets()
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

/// <summary>Generates and consumes the real PostgreSql provider's compiled relational metadata.</summary>
public sealed class CompiledPostgreSqlContext : CompiledTreeContext
{
    /// <summary>Creates a design-time context without opening a database connection.</summary>
    public CompiledPostgreSqlContext() : this(
        new DbContextOptionsBuilder<CompiledPostgreSqlContext>()
            .ConfigureTestWarnings()
            .UseNpgsql("Host=localhost;Database=compiled;Username=unused")
            .UseNestedSets()
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

/// <summary>Generates and consumes the real SqlServer provider's compiled relational metadata.</summary>
public sealed class CompiledSqlServerContext : CompiledTreeContext
{
    /// <summary>Creates a design-time context without opening a database connection.</summary>
    public CompiledSqlServerContext() : this(
        new DbContextOptionsBuilder<CompiledSqlServerContext>()
            .ConfigureTestWarnings()
            .UseSqlServer(
                "Server=localhost;Database=compiled;User ID=unused;Password=unused;"
                + "TrustServerCertificate=True;MultipleActiveResultSets=False")
            .UseNestedSets()
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
