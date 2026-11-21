namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies an injected rollback failure independently of the original operation failure.</summary>
public sealed class InjectedCleanupException : Exception;
