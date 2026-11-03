namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Distinguishes tracked-owner membership without changing the rows written by an import.</summary>
public enum PreparedOwnerMode
{
    /// <summary>Leaves the persisted owner outside the tracker.</summary>
    Untracked,

    /// <summary>Loads the existing owner with an initially empty inverse collection.</summary>
    LoadedEmpty,

    /// <summary>Loads the existing owner and supplies every validated input through its inverse collection.</summary>
    Prepared,
}
