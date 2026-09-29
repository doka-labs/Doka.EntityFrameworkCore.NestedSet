namespace Doka.EntityFrameworkCore.NestedSet.Samples;

/// <summary>Identifies the documented local database routes for the console samples.</summary>
internal enum SampleProvider
{
    /// <summary>Uses Doka with the shared MariaDB 11.8 developer server.</summary>
    MariaDb,

    /// <summary>Uses Doka with the shared MySQL 8.4 developer server.</summary>
    MySql,

    /// <summary>Uses a persistent, sample-owned SQLite file.</summary>
    Sqlite,
}
