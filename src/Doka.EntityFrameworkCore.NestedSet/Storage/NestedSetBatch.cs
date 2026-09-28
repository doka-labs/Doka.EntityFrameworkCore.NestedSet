namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Defines the shared bound on per-row parameters in structural update statements.</summary>
internal static class NestedSetBatch
{
    /// <summary>Bounds per-row SQL generation, expression compilation, and parameter-buffer allocation.</summary>
    // WHY: A row can contribute several CASE parameters or a UNION branch. Keep the measured 64-row work
    // budget independent of any provider's scalar-parameter ceiling; it is not SQLite's current parameter limit.
    internal const int MaximumRows = 64;
}
