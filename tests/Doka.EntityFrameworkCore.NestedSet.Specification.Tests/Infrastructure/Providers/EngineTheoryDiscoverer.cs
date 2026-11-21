namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Omits excluded methods while preserving xUnit's immediate and delayed data-enumeration behavior.</summary>
public sealed class EngineTheoryDiscoverer : TheoryDiscoverer
{
    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute
    )
    {
        var engine = EngineTestSelection.ResolveEngine(testMethod.TestClass.Class);

        if (factAttribute is EngineTheoryAttribute conditional
            && !EngineTestSelection.AppliesToEngine(engine, conditional.ExcludedEngines, conditional.Reason))
        {
            return ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>([]);
        }

        // WHY: Do not pre-read factories to decide ownership; xUnit must enumerate each source only in its own phase.
        return base.Discover(discoveryOptions, testMethod, factAttribute);
    }
}
