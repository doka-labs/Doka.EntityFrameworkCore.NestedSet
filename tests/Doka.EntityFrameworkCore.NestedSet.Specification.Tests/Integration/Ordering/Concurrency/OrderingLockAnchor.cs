namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Stores a stable scope identity used by the application-owned lock protocol.</summary>
/// <typeparam name="TScope">The anchor's native scope-key type.</typeparam>
internal sealed class OrderingLockAnchor<TScope>
    where TScope : notnull
{
    /// <summary>Gets or sets the immutable anchor key.</summary>
    public required TScope Id { get; set; }
}
