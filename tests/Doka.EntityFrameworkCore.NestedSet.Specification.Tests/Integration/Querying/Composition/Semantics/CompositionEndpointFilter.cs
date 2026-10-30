namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Names the public endpoint filter being qualified so theory cases remain readable in Rider.</summary>
public enum CompositionEndpointFilter
{
    /// <summary>Requires every visible allowed direct edge between the selected endpoints.</summary>
    Path,

    /// <summary>Requires one visible matching ancestor without demanding a complete visible path.</summary>
    Any,

    /// <summary>Selects the nearest eligible ancestor before checking its value and complete path.</summary>
    Nearest,
}
