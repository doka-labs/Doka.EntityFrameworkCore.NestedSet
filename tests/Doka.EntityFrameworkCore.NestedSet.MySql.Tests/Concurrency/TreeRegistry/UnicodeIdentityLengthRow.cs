namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>
/// Captures actual stored values and native length functions independently of EF identity comparison.
/// </summary>
internal sealed class UnicodeIdentityLengthRow
{
    /// <summary>Gets or sets the exact stored partition decoded using the table's UTF-8 representation.</summary>
    public string Scope { get; set; } = "";

    /// <summary>Gets or sets the exact stored stable tree identity.</summary>
    public string TreeId { get; set; } = "";

    /// <summary>Gets or sets the native number of characters in the stored partition.</summary>
    public int ScopeCharacters { get; set; }

    /// <summary>Gets or sets the native number of octets in the stored partition.</summary>
    public int ScopeBytes { get; set; }

    /// <summary>Gets or sets the native number of characters in the stored TreeId.</summary>
    public int TreeCharacters { get; set; }

    /// <summary>Gets or sets the native number of octets in the stored TreeId.</summary>
    public int TreeBytes { get; set; }
}
