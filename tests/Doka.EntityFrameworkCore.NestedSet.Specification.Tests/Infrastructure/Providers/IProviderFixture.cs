namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies the immutable engine owned by a concrete provider fixture.</summary>
public interface IProviderFixture
{
    /// <summary>Gets the engine used by every case in this concrete suite.</summary>
    string Engine { get; }
}
