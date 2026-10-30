namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Carries mapped direct-edge aliases inside a translated query without materializing either entity.</summary>
/// <typeparam name="TEntity">The configured hierarchy entity.</typeparam>
internal sealed class NestedSetQueryEdge<TEntity>
    where TEntity : class
{
    /// <summary>Gets or initializes the visible direct principal.</summary>
    public required TEntity Parent { get; init; }

    /// <summary>Gets or initializes the visible direct dependent.</summary>
    public required TEntity Child { get; init; }
}
