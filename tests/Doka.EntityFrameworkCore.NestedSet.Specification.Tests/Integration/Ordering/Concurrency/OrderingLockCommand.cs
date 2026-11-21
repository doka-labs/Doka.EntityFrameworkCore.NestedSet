namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves one observed SQL command and its typed parameter values.</summary>
/// <param name="Sql">The actual provider command text.</param>
/// <param name="Parameters">Immutable snapshots of the parameters sent with this command.</param>
internal sealed record OrderingLockCommand(
    string Sql,
    OrderingLockParameter[] Parameters
);
