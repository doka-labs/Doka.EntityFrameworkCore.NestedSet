namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Defines the engine identity at the concrete fixture's type boundary.</summary>
public interface IProviderEngine
{
    /// <summary>Gets the exact engine name accepted by the database helpers.</summary>
    static abstract string Name { get; }
}
