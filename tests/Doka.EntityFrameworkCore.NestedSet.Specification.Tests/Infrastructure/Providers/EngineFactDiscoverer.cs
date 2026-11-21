namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Omits explicitly excluded facts before xUnit constructs a test case or any fixture.</summary>
public sealed class EngineFactDiscoverer : FactDiscoverer
{
    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute
    )
    {
        var engine = EngineTestSelection.ResolveEngine(testMethod.TestClass.Class);

        if (factAttribute is EngineFactAttribute conditional
            && !EngineTestSelection.AppliesToEngine(engine, conditional.ExcludedEngines, conditional.Reason))
        {
            return ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>([]);
        }

        return base.Discover(discoveryOptions, testMethod, factAttribute);
    }
}
