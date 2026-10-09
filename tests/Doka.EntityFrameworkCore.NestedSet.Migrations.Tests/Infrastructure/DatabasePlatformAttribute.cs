using Xunit.v3;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Checks database applicability before mixed-engine or explicitly engine-bound migration tests run.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class DatabasePlatformAttribute : BeforeAfterTestAttribute
{
    private readonly string? _engine;

    /// <summary>Uses an explicit engine, or the current theory row's named engine argument.</summary>
    /// <param name="engine">The fixed database engine; omit it for mixed-engine theories.</param>
    public DatabasePlatformAttribute(
        string? engine = null
    )
    {
        _engine = engine;
    }

    /// <inheritdoc />
    public override void Before(
        MethodInfo methodUnderTest,
        IXunitTest test
    ) => SqlServerTestPlatform.BeforeDatabaseTest(ResolveEngine(methodUnderTest, test.TestMethodArguments));

    /// <summary>Reads actual row arguments without invoking a data factory or interpreting test names.</summary>
    internal string? ResolveEngine(
        MethodInfo method,
        object?[] arguments
    )
    {
        if (_engine is not null)
        {
            return _engine;
        }

        var parameters = method.GetParameters();

        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index] is { Name: "engine", ParameterType: var type } && type == typeof(string))
            {
                return arguments[index] as string
                    ?? throw new InvalidOperationException("A database theory must provide its engine argument.");
            }
        }

        return null;
    }
}
