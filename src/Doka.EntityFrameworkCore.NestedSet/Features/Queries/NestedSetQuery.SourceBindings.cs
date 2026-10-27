namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
{
    /// <summary>Gets the selected adapter's exact completed-source operator for endpoint provenance.</summary>
    private MethodInfo? SourceBindingMethod() => _capabilities.CompletedSourceMethod;

    /// <summary>Gets the selected adapter's exact ordered inner-join operator for endpoint provenance.</summary>
    private MethodInfo? OrderedJoinMethod() => _capabilities.OrderedJoinMethod;
}
