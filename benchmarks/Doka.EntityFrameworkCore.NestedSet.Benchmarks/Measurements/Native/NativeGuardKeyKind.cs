namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies the actual key storage admitted by the native tracked-identity probe.</summary>
public enum NativeGuardKeyKind
{
    /// <summary>Uses an integer key and an explicitly converted string tree identity.</summary>
    NativeNumber,

    /// <summary>Uses native GUID keys and tree identities.</summary>
    NativeIdentifier,

    /// <summary>Uses native ordinal string keys and GUID tree identities.</summary>
    OrdinalText,

    /// <summary>Stores integer keys through explicit string conversion.</summary>
    ConvertedInteger,
}
