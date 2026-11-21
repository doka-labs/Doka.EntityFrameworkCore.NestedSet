namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exposes an engine-bound resource without coupling shared suites to a concrete provider.</summary>
/// <typeparam name="TResource">The database or test resource owned by the fixture.</typeparam>
public interface IProviderFixture<out TResource> : IProviderFixture
{
    /// <summary>Gets the fixture-owned resource; individual cases must not dispose it.</summary>
    TResource Value { get; }
}
