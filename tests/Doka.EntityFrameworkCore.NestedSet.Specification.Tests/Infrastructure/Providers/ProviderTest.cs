namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Reads engine identity from the fixture supplied by the concrete provider suite.</summary>
[ProviderDatabasePlatform]
public abstract class ProviderTest
{
    /// <summary>Retains the immutable engine identity for shared test methods.</summary>
    /// <param name="fixture">The resource owner supplied through the concrete xUnit constructor.</param>
    protected ProviderTest(
        IProviderFixture fixture
    )
    {
        Engine = fixture.Engine;
    }

    /// <summary>Gets the engine bound to this concrete suite's fixture.</summary>
    protected string Engine { get; }
}
