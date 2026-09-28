namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Controls whether explicit placement may override the configured sibling order.</summary>
public enum NestedSetOrderMode
{
    /// <summary>Requires sibling placement to follow the configured property order.</summary>
    Strict = 0,

    /// <summary>Allows explicit placement to override property order for the requested operation.</summary>
    AllowManualPlacement = 1,
}
