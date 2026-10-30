namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Stores ordinary application rows related to one scoped hierarchy principal.</summary>
internal sealed class CompositionNavigationItem
{
    /// <summary>Gets or sets the scope shared with the related principal.</summary>
    internal Guid Scope { get; set; }

    /// <summary>Gets or sets the scope-local application-row identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the scope-local hierarchy principal key.</summary>
    internal int NodeId { get; set; }

    /// <summary>Gets or sets the ordinary collection payload.</summary>
    internal string Label { get; set; } = string.Empty;
}
