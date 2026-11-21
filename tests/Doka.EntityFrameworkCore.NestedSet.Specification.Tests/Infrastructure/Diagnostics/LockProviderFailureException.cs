namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Supplies a stable database failure instance for exception-preservation and privacy assertions.</summary>
internal sealed class LockProviderFailureException : DbException
{
    /// <summary>Creates a database failure whose sensitive message must never enter library telemetry.</summary>
    internal LockProviderFailureException() : base("private-provider-message-and-connection-details") { }
}
