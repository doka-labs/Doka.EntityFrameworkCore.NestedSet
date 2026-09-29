namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies the database engine owned by an observational benchmark run.</summary>
public enum BenchmarkEngine
{
    /// <summary>Uses the canonical MySQL container and Doka provider.</summary>
    MySql,

    /// <summary>Uses the canonical MariaDB container and Doka provider.</summary>
    MariaDb,

    /// <summary>Uses the canonical PostgreSQL container.</summary>
    PostgreSql,

    /// <summary>Uses the canonical SQL Server container.</summary>
    SqlServer,

    /// <summary>Uses a SQLite database kept alive by the scenario connection.</summary>
    SqliteMemory,

    /// <summary>Uses an owned temporary SQLite file within the run directory.</summary>
    SqliteFile,
}
