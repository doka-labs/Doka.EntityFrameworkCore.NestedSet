namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies both a committed revision and its exact working source state.</summary>
public sealed record BenchmarkSourceIdentity
{
    /// <summary>Gets the current Git commit SHA.</summary>
    public required string Commit { get; init; }

    /// <summary>Gets whether Git reports staged, unstaged or untracked changes.</summary>
    public required bool Dirty { get; init; }

    /// <summary>Gets a SHA256 fingerprint of the current tracked and nonignored working files.</summary>
    public required string Fingerprint { get; init; }
}
