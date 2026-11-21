namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Exposes test-owned model variation without requiring a distinct EF service graph for each test family.
/// </summary>
internal interface ITestModelVariant
{
    /// <summary>Gets the immutable identity separating variants of the same context type.</summary>
    object ModelVariant { get; }
}
