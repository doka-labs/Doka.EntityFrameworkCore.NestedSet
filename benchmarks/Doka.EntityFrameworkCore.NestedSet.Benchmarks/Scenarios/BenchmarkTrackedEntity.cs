namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Represents unchanged application entries that participate in save and mutation tracker scans.</summary>
public sealed class BenchmarkTrackedEntity
{
    /// <summary>Gets or sets the assigned key of the tracked application entity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets application data retained while the hierarchy operation executes.</summary>
    public string Payload { get; set; } = string.Empty;
}
