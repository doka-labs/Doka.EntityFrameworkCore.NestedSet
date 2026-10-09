namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares the exact workload size and identities of the two concurrent writer contracts.</summary>
internal static class ConcurrentCapacityTestSupport
{
    /// <summary>Gets the number of writers that every concurrent scenario must actually complete.</summary>
    internal const int WriterCount = 64;

    /// <summary>Gets the scope shared by all writers so tree identity, rather than scope, separates trees.</summary>
    internal const int Scope = 7;

    /// <summary>Creates a deterministic distinct tree identity without changing the shared scope.</summary>
    internal static Guid TreeId(
        int value
    ) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
