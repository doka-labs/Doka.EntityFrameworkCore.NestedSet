namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Distinguishes model identities from their converted relational text representation.</summary>
/// <param name="Value">The provider text retained without normalization.</param>
internal readonly record struct UnicodeIdentityValue(string Value);
