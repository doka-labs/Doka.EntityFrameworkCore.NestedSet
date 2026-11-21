namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses EF's built-in enum-to-string conversion for assigned node identities.</summary>
internal enum EnumTextKey
{
    Root,
    Child,
    Other,
    Leaf,
    Missing,
}
