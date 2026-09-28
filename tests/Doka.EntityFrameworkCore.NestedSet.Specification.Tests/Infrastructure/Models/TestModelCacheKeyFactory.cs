namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exposes test-owned model variation without requiring a distinct EF service graph for each test family.</summary>
internal interface ITestModelVariant
{
    /// <summary>Gets the immutable identity separating variants of the same context type.</summary>
    object ModelVariant { get; }
}

/// <summary>Shares one EF singleton implementation across all deliberate mapping variants in the test suite.</summary>
public sealed class TestModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(
        DbContext context,
        bool designTime
    )
    {
        // WHY: Separate replacement types duplicated provider singleton graphs even though their algorithms matched.
        // The context type still isolates independent test families; the variant preserves each family's exact model.
        return context is ITestModelVariant variant
            ? (context.GetType(), variant.ModelVariant, designTime)
            : (context.GetType(), designTime);
    }
}
