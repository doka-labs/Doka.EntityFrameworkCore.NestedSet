namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Selects whether the source membership group itself can supply a supervisor.</summary>
internal enum SearchStart
{
    /// <summary>Includes a qualifying person in the source membership's own group.</summary>
    IncludeSelf,

    /// <summary>Searches only groups strictly above the source membership.</summary>
    ParentsOnly,
}
