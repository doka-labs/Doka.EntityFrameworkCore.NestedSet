namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks the concrete fixture's database host while preserving metadata-only provider tests.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ProviderDatabasePlatformAttribute : BeforeAfterTestAttribute
{
    /// <inheritdoc />
    public override void Before(
        MethodInfo methodUnderTest,
        IXunitTest test
    ) => SqlServerTestPlatform.BeforeDatabaseTest(ResolveDatabaseEngine(test.TestMethod.TestClass.Class, methodUnderTest));

    /// <summary>Reads the concrete fixture contract without constructing database or metadata resources.</summary>
    internal static string? ResolveDatabaseEngine(
        Type suite,
        MethodInfo method
    )
    {
        var engine = EngineTestSelection.ResolveEngine(suite);

        // WHY: ProviderResources only supplies engine identity; temporal and lifecycle tests still open databases.
        // Only an explicitly database-independent suite or method may bypass the host check.
        return suite.IsDefined(typeof(DatabaseIndependentAttribute), inherit: true)
            || method.IsDefined(typeof(DatabaseIndependentAttribute), inherit: true) ? null : engine;
    }
}
