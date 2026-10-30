using Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies executable fixture isolation and local collection registration.</summary>
public sealed class ProviderFixtureIsolationTests
{
    /// <summary>Concurrent provider contexts retain separate fixture owners when one assembly is disposed.</summary>
    [Fact]
    public async Task ProviderFixtureChainsRemainIsolatedDuringConcurrentDisposal()
    {
        // Arrange
        var firstClass = CreateTestMethod("MySql", nameof(FixtureContextProbe.ContextAssemblyHasAnOwner)).TestClass;
        var secondClass = CreateTestMethod("PostgreSql", nameof(FixtureContextProbe.ContextAssemblyHasAnOwner)).TestClass;
        await using var firstAssembly = new FixtureMappingManager("Assembly");
        await using var secondAssembly = new FixtureMappingManager("Assembly");
        var fixtureTypes = new[] { typeof(TestDatabaseServers), typeof(FixtureDisposalProbe) };

        // WHY: Public registration creates separate lazy owners; containers start only when a database is requested.
        await firstAssembly.InitializeAsync(fixtureTypes, createInstances: true);
        await secondAssembly.InitializeAsync(fixtureTypes, createInstances: true);
        var firstServers = Assert.IsType<TestDatabaseServers>(
            await firstAssembly.GetFixture(typeof(TestDatabaseServers)));

        var secondServers = Assert.IsType<TestDatabaseServers>(
            await secondAssembly.GetFixture(typeof(TestDatabaseServers)));

        var firstCleanup = Assert.IsType<FixtureDisposalProbe>(
            await firstAssembly.GetFixture(typeof(FixtureDisposalProbe)));

        var secondCleanup = Assert.IsType<FixtureDisposalProbe>(
            await secondAssembly.GetFixture(typeof(FixtureDisposalProbe)));

        await using var firstCollection = new FixtureMappingManager("Collection", firstAssembly);
        await using var secondCollection = new FixtureMappingManager("Collection", secondAssembly);
        await using var firstMappings = new FixtureMappingManager("Class", firstCollection);
        await using var secondMappings = new FixtureMappingManager("Class", secondCollection);
        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var first = Task.Run(
            async () =>
            {
                try
                {
                    TestContext.SetForTestClass(firstClass, TestEngineStatus.Running, cancellationToken, firstMappings);
                    var before = await TestDatabaseServers.GetCurrentAsync();
                    firstReady.TrySetResult();
                    await secondReady.Task.WaitAsync(cancellationToken);
                    var interleaved = await TestDatabaseServers.GetCurrentAsync();
                    await firstAssembly.DisposeAsync();

                    return (Before: before, Interleaved: interleaved);
                }
                finally
                {
                    // WHY: Release the peer if lookup or cleanup fails so an isolation regression cannot hang the suite.
                    firstReady.TrySetResult();
                    firstDisposed.TrySetResult();
                }
            },
            cancellationToken);

        var second = Task.Run(
            async () =>
            {
                try
                {
                    TestContext.SetForTestClass(
                        secondClass,
                        TestEngineStatus.Running,
                        cancellationToken,
                        secondMappings);

                    var before = await TestDatabaseServers.GetCurrentAsync();
                    secondReady.TrySetResult();
                    await firstReady.Task.WaitAsync(cancellationToken);
                    var interleaved = await TestDatabaseServers.GetCurrentAsync();
                    await firstDisposed.Task.WaitAsync(cancellationToken);
                    var afterDisposal = await TestDatabaseServers.GetCurrentAsync();

                    return (Before: before, Interleaved: interleaved, AfterDisposal: afterDisposal);
                }
                finally
                {
                    secondReady.TrySetResult();
                }
            },
            cancellationToken);

        await Task.WhenAll(first, second);
        var firstResult = await first;
        var secondResult = await second;

        // Assert
        Assert.NotSame(firstServers, secondServers);
        Assert.Same(firstServers, firstResult.Before);
        Assert.Same(firstServers, firstResult.Interleaved);
        Assert.Same(secondServers, secondResult.Before);
        Assert.Same(secondServers, secondResult.Interleaved);
        Assert.Same(secondServers, secondResult.AfterDisposal);
        Assert.True(firstCleanup.IsDisposed);
        Assert.False(secondCleanup.IsDisposed);
    }

    /// <summary>Unit cases resolve named collections locally and preserve allocation isolation.</summary>
    [Fact]
    public void UnitNamedCollectionsResolveToLocalDefinitions()
    {
        // Arrange
        var assembly = typeof(ProviderFixtureIsolationTests).Assembly;
        var types = assembly.GetExportedTypes();

        // Act
        var referenced = types
            .Where(type => !type.IsAbstract
                && type
                    .GetMethods()
                    .Any(method =>
                        method.CustomAttributes.Any(attribute =>
                            typeof(IFactAttribute).IsAssignableFrom(attribute.AttributeType))))
            .SelectMany(type => type.GetCustomAttributes<CollectionAttribute>(inherit: true))
            .Select(attribute => attribute.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var definitions = types
            .Select(type => (Type: type, Definition: type.GetCustomAttribute<CollectionDefinitionAttribute>()))
            .Where(entry => entry.Definition is not null)
            .ToArray();

        // Assert
        Assert.Contains("Allocation measurements", referenced);
        Assert.All(
            new[]
            {
                typeof(TypedPropertyReadTests),
                typeof(TypedProviderComparerTests),
                typeof(KeyComparerTests),
                typeof(InsertionRefreshAllocationTests),
                typeof(TrackedIdentityIterationAllocationTests),
                typeof(SignalReportSnapshotAllocationTests),
                typeof(SignalPathConstructionAllocationTests),
                typeof(MappedMemberConstructionTests),
            },
            type => Assert.Equal(
                "Allocation measurements",
                type.GetCustomAttribute<CollectionAttribute>()
                    ?.Name));

        foreach (var name in referenced)
        {
            var local = Assert.Single(definitions, entry => entry.Definition!.Name == name);
            Assert.Same(assembly, local.Type.Assembly);

            if (name == "Allocation measurements")
            {
                Assert.True(local.Definition!.DisableParallelization);
            }
        }
    }

    /// <summary>The actual xUnit metadata path propagates the measurement collection's filter trait.</summary>
    [Fact]
    public void AllocationCollectionTraitsReachConcreteTestMethods()
    {
        // Arrange
        var assembly = typeof(ProviderFixtureIsolationTests).Assembly;
        var testAssembly = new XunitTestAssembly(assembly, null, assembly.Location);
        var collection = new XunitTestCollection(
            testAssembly,
            typeof(AllocationMeasurementIsolation),
            true,
            "Allocation measurements");

        var testClass = new XunitTestClass(typeof(TypedPropertyReadTests), collection);
        var method = typeof(TypedPropertyReadTests)
            .GetMethod(nameof(TypedPropertyReadTests.IntegerParentReadsLiveNullablePresence))!;

        // Act
        var metadata = new XunitTestMethod(testClass, method, []);

        // Assert
        Assert.Contains("Allocation", metadata.Traits["Category"]);
    }

    /// <summary>Unicode validation remains instrumented while only its fixed allocation budget is separated.</summary>
    [Fact]
    public void UnicodeValidationRemainsCoveredWhileBudgetRunsUninstrumented()
    {
        // Arrange
        var assembly = typeof(ProviderFixtureIsolationTests).Assembly;
        var testAssembly = new XunitTestAssembly(assembly, null, assembly.Location);
        var collection = new XunitTestCollection(testAssembly, null, false, "Unicode semantics");
        var testClass = new XunitTestClass(typeof(UnicodeIdentityLengthTests), collection);
        var allocation = typeof(UnicodeIdentityLengthTests)
            .GetMethod(nameof(UnicodeIdentityLengthTests.WarmCharacterCountingDoesNotAllocate))!;

        var validation = typeof(UnicodeIdentityLengthTests)
            .GetMethod(nameof(UnicodeIdentityLengthTests.EveryUnpairedSurrogateCodeUnitIsRejected))!;

        // Act
        var budgetMetadata = new XunitTestMethod(testClass, allocation, []);
        var validationMetadata = new XunitTestMethod(testClass, validation, []);

        // Assert
        Assert.Contains("Allocation", budgetMetadata.Traits["Category"]);
        Assert.False(validationMetadata.Traits.TryGetValue("Category", out var categories)
            && categories.Contains("Allocation", StringComparer.Ordinal));
    }

    /// <summary>GC lifetime controls retain their assertions in the required uninstrumented measurement partition.</summary>
    /// <param name="methodName">The released-root or intentionally live-root registration control.</param>
    [Theory]
    [InlineData(nameof(OptionsExtensionTests.RegistrationDoesNotRetainACollectibleAdapter))]
    [InlineData(nameof(OptionsExtensionTests.LiveRegistrationRetainsItsCollectibleAdapter))]
    public void RegistrationLifetimeControlsRunWithoutCoverage(string methodName)
    {
        // Arrange
        var assembly = typeof(ProviderFixtureIsolationTests).Assembly;
        var testAssembly = new XunitTestAssembly(assembly, null, assembly.Location);
        var collection = new XunitTestCollection(testAssembly, null, false, "Registration lifetime");
        var testClass = new XunitTestClass(typeof(OptionsExtensionTests), collection);
        var method = typeof(OptionsExtensionTests).GetMethod(methodName)!;

        // Act
        var metadata = new XunitTestMethod(testClass, method, []);

        // Assert
        Assert.Contains("Allocation", metadata.Traits["Category"]);
    }

    /// <summary>Creates a concrete subclass in an isolated assembly with a real provider assembly identity.</summary>
    private static XunitTestMethod CreateTestMethod(
        string project,
        string methodName
    )
    {
        // WHY: Dynamic owners exercise cross-assembly reflection without adding bad cases to provider projects.
        var name = new AssemblyName($"Doka.EntityFrameworkCore.NestedSet.{project}.Tests");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name.Name!);
        var builder = module.DefineType("InheritedProviderProbe", TypeAttributes.Public, typeof(FixtureContextProbe));
        var type = builder.CreateType();
        var testAssembly = new XunitTestAssembly(assembly, null, assemblyPath: string.Empty);
        var collection = new XunitTestCollection(testAssembly, null, false, "Inherited provider probe");
        var testClass = new XunitTestClass(type, collection);

        return new XunitTestMethod(testClass, type.GetMethod(methodName)!, []);
    }

    /// <summary>Records actual assembly mapping cleanup without creating a database or container.</summary>
    public sealed class FixtureDisposalProbe : IAsyncDisposable
    {
        /// <summary>Allows xUnit to create a distinct sentinel for each assembly mapping.</summary>
        public FixtureDisposalProbe() { }

        /// <summary>Gets whether this fixture's owning mapping has disposed it.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Records cleanup of this exact fixture instance.</summary>
        public ValueTask DisposeAsync()
        {
            IsDisposed = true;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Supplies ordinary inherited metadata for separate executable assembly contexts.</summary>
    // WHY: Abstract metadata subjects are not Unit suites; emitted provider leaves expose their real owner identity.
    public abstract class FixtureContextProbe
    {
        /// <summary>Represents an ordinary fact inherited by either emitted provider owner.</summary>
        [Fact]
        public void ContextAssemblyHasAnOwner()
        {
            // Arrange
            var assembly = GetType()
                .Assembly
                .GetName()
                .Name!;

            // Act
            var engines = ProviderTestContract.OwnedEngines(assembly);

            // Assert
            Assert.NotEmpty(engines);
        }
    }
}
