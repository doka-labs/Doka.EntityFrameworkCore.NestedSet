using Xunit.Runner.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class EngineTestDiscoveryTests
{
    /// <summary>Creates a fixture-taking metadata owner with a real provider assembly identity.</summary>
    private static XunitTestMethod CreateTestMethod(
        string engine,
        string methodName,
        Type[]? fixtureTypes = null,
        bool databaseIndependent = false
    )
    {
        var project = engine == "MariaDb" ? "MySql" : engine;
        var name = new AssemblyName($"Doka.EntityFrameworkCore.NestedSet.{project}.Tests");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name.Name!);
        var builder = module.DefineType("InheritedEngineProbe", TypeAttributes.Public, typeof(DiscoveryProbe));

        if (databaseIndependent)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(DatabaseIndependentAttribute).GetConstructor(Type.EmptyTypes)!, []));
        }

        fixtureTypes ??= [GetFixtureType(engine)];

        foreach (var fixture in fixtureTypes)
        {
            builder.AddInterfaceImplementation(typeof(IClassFixture<>).MakeGenericType(fixture));
        }

        var constructor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, fixtureTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(
            OpCodes.Call,
            typeof(DiscoveryProbe).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null)!);
        il.Emit(OpCodes.Ret);
        var type = builder.CreateType();

        var testAssembly = new XunitTestAssembly(assembly, null, assemblyName: name.FullName);
        var collection = new XunitTestCollection(testAssembly, null, false, "Fixture-owned engine probe");
        var testClass = new XunitTestClass(type, collection);

        return new XunitTestMethod(testClass, type.GetMethod(methodName)!, []);
    }

    /// <summary>Uses only the known closed fixture markers, independently of the current Unit assembly.</summary>
    private static Type GetFixtureType(
        string engine
    ) => engine switch
    {
        "MySql" => typeof(ProviderFixture<HarmlessResource, MySqlEngine>),
        "MariaDb" => typeof(ProviderFixture<HarmlessResource, MariaDbEngine>),
        "PostgreSql" => typeof(ProviderFixture<HarmlessResource, PostgreSqlEngine>),
        "SqlServer" => typeof(ProviderFixture<HarmlessResource, SqlServerEngine>),
        "Sqlite" => typeof(ProviderFixture<HarmlessResource, SqliteEngine>),
        _ => throw new InvalidOperationException($"Unknown metadata probe engine '{engine}'."),
    };

    /// <summary>Selects xUnit's documented theory-enumeration option without altering ambient test context.</summary>
    private static ITestFrameworkDiscoveryOptions CreateDiscoveryOptions(
        bool preEnumerate
    )
    {
        var options = TestFrameworkOptions.ForDiscovery(
            new TestAssemblyConfiguration());

        options.SetValue(TestOptionsNames.Discovery.PreEnumerateTheories, preEnumerate);

        return options;
    }
}
