namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Names final EF tracking policies without coupling them to individual projection aliases.</summary>
public enum CompositionTrackingPolicy
{
    /// <summary>Materializes separate instances without adding them to the context tracker.</summary>
    NoTracking,

    /// <summary>Uses context-wide identity tracking and existing tracked instances.</summary>
    Tracking,

    /// <summary>Resolves identity within the result without growing the context tracker.</summary>
    IdentityResolution,
}
