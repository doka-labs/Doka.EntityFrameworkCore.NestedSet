namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Describes the deterministic adjacency distribution of each benchmark tree.</summary>
public enum BenchmarkShape
{
    /// <summary>One root has every other node as a direct child.</summary>
    Wide,

    /// <summary>Every non-leaf node has exactly one child.</summary>
    Deep,

    /// <summary>Nodes form a breadth-first binary tree.</summary>
    Balanced,
}
