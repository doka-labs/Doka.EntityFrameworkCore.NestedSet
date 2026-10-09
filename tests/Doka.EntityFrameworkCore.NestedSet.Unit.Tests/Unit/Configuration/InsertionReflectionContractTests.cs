using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks the complete production framework resolver independently of database lifecycle tests.</summary>
public sealed class InsertionReflectionContractTests
{
    /// <summary>Registration validates all map shapes without compiling their model-owned operations.</summary>
    [Fact]
    public void RequiredInsertionReflectionContractResolvesEveryMemberAndGenericShape()
    {
        // Arrange
        var assembly = typeof(IUpdateEntry).Assembly;
        Type[] keyTypes =
        [
            typeof(int),
            typeof(Guid),
            typeof(string),
            typeof(byte[]),
            typeof(IReadOnlyList<object>),
        ];

        var definitions = new[] { "IdentityMap`1", "NullableKeyIdentityMap`1" };
        var cacheField = typeof(NestedSetInsertionContract).GetField(
            "_maps",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Act
        var contract = NestedSetInsertionContract.Bind(assembly);

        // Assert
        Assert.NotNull(contract.Entry);
        Assert.NotNull(contract.Manager);
        Assert.NotNull(contract.SetProperty);
        Assert.NotNull(contract.FindMap);
        Assert.NotNull(contract.Dependents);
        Assert.NotNull(contract.StopTracking);
        Assert.NotNull(contract.UpdateReferences);
        Assert.NotNull(contract.Setter);
        var cache = Assert.IsType<ConditionalWeakTable<Type, NestedSetInsertionContract.KeyMap>>(
            cacheField.GetValue(contract));

        foreach (var definition in definitions)
        {
            var mapDefinition = assembly.GetType(
                "Microsoft.EntityFrameworkCore.ChangeTracking.Internal." + definition)!;

            foreach (var keyType in keyTypes)
            {
                Assert.False(cache.TryGetValue(mapDefinition.MakeGenericType(keyType), out _));
            }
        }
    }

    /// <summary>Only requested native key operations enter the weak cache.</summary>
    [Theory]
    [InlineData("IdentityMap`1", typeof(int))]
    [InlineData("IdentityMap`1", typeof(Guid))]
    [InlineData("IdentityMap`1", typeof(string))]
    [InlineData("IdentityMap`1", typeof(byte[]))]
    [InlineData("IdentityMap`1", typeof(IReadOnlyList<object>))]
    [InlineData("NullableKeyIdentityMap`1", typeof(int))]
    [InlineData("NullableKeyIdentityMap`1", typeof(Guid))]
    [InlineData("NullableKeyIdentityMap`1", typeof(string))]
    [InlineData("NullableKeyIdentityMap`1", typeof(byte[]))]
    [InlineData("NullableKeyIdentityMap`1", typeof(IReadOnlyList<object>))]
    public void ClosedMapOperationsAreCompiledOnlyWhenRequestedAndCached(
        string definition,
        Type keyType
    )
    {
        // Arrange
        var assembly = typeof(IUpdateEntry).Assembly;
        var contract = NestedSetInsertionContract.Bind(assembly);
        var mapType = assembly.GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal." + definition)!
            .MakeGenericType(keyType);

        var cacheField = typeof(NestedSetInsertionContract).GetField(
            "_maps",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var cache = (ConditionalWeakTable<Type, NestedSetInsertionContract.KeyMap>)cacheField.GetValue(contract)!;
        var initiallyCached = cache.TryGetValue(mapType, out _);

        // Act
        var operations = contract.Map(mapType);
        var repeated = contract.Map(mapType);

        // Assert
        Assert.False(initiallyCached);
        Assert.Same(operations, repeated);
        Assert.True(cache.TryGetValue(mapType, out var cached));
        Assert.Same(operations, cached);
        Assert.NotNull(operations.Remove);
        Assert.NotNull(operations.RemoveCurrent);
        Assert.NotNull(operations.Add);
        Assert.NotNull(operations.Dependents);
        Assert.NotNull(operations.Find);
        Assert.NotNull(operations.Entries);
    }

    /// <summary>Typed readers capture installed property snapshots when current composite values change.</summary>
    [Fact]
    public void RelationshipReadersUseCapturedMetadataAndInstalledSnapshot()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<SnapshotContext>().UseSqlite("Data Source=:memory:").Options;
        using var context = new SnapshotContext(options);
        var contract = NestedSetInsertionContract.Bind(typeof(IUpdateEntry).Assembly);
        var entity = new SnapshotEntity
        {
            First = 1,
            Second = 2,
        };

        var entry = contract.Entry(context.Attach(entity));
        var first = entry.EntityType.FindProperty(nameof(SnapshotEntity.First))!;
        var second = entry.EntityType.FindProperty(nameof(SnapshotEntity.Second))!;
        var firstReader = contract.RelationshipReader<int>(first);
        var secondReader = contract.RelationshipReader<int>(second);
        entity.First = 11;
        entity.Second = 22;

        // Act
        var snapshot = (First: firstReader(entry), Second: secondReader(entry));

        // Assert
        Assert.Equal(1, snapshot.First);
        Assert.Equal(2, snapshot.Second);
        Assert.Equal(11, entry.GetCurrentValue<int>(first));
        Assert.Equal(22, entry.GetCurrentValue<int>(second));
    }

    /// <summary>An incompatible assembly identifies its version and exact member before a context exists.</summary>
    [Fact]
    public void IncompatibleFrameworkHasARegistrationDiagnostic()
    {
        // Arrange
        var assembly = typeof(object).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        // Act
        var error = Record.Exception(() => NestedSetInsertionContract.Bind(assembly));

        // Assert
        var incompatible = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(version, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("InternalEntityEntry", incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets", incompatible.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Discard", incompatible.Message, StringComparison.Ordinal);
    }

    /// <summary>Options registration validates its framework contract without a provider, model or context.</summary>
    [Fact]
    public void RegistrationValidatesWithoutCreatingAContext()
    {
        // Arrange
        var loadContext = new AssemblyLoadContext(
            nameof(RegistrationValidatesWithoutCreatingAContext),
            isCollectible: true);

        loadContext.Resolving += (_, name) => AssemblyLoadContext.Default.Assemblies.SingleOrDefault(assembly =>
            AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), name));

        try
        {
            // WHY: A fresh production assembly proves the registration call initializes the contract even if
            // another test already used the default assembly's singleton, without changing process-global hooks.
            var assembly = loadContext.LoadFromAssemblyPath(typeof(NestedSetInsertionContract).Assembly.Location);
            var contractType = assembly.GetType(typeof(NestedSetInsertionContract).FullName!, throwOnError: true)!;
            var lazy = contractType.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;

            var created = lazy
                .GetType()
                .GetProperty("IsValueCreated")!;

            var registrationType = assembly.GetType(
                typeof(NestedSetDbContextOptionsBuilderExtensions).FullName!,
                throwOnError: true)!;

            var registration = registrationType.GetMethod(
                nameof(NestedSetDbContextOptionsBuilderExtensions.UseNestedSets),
                [typeof(DbContextOptionsBuilder)])!;

            var options = new DbContextOptionsBuilder();
            var initiallyCreated = (bool)created.GetValue(lazy)!;

            // Act
            var registered = registration.Invoke(null, [options]);

            // Assert
            Assert.False(initiallyCreated);
            Assert.Same(options, registered);
            Assert.True((bool)created.GetValue(lazy)!);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private sealed class SnapshotContext : DbContext
    {
        /// <summary>Creates a context whose compound key has native relationship snapshots.</summary>
        /// <param name="options">The in-memory model's provider options.</param>
        public SnapshotContext(
            DbContextOptions<SnapshotContext> options
        ) : base(options) { }

        /// <summary>Configures the two-property principal key used by the typed snapshot control.</summary>
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<SnapshotEntity>()
            .HasKey(entity => new
            {
                entity.First,
                entity.Second,
            });
    }

    private sealed class SnapshotEntity
    {
        /// <summary>Gets or sets the first compound principal-key component.</summary>
        public int First { get; set; }

        /// <summary>Gets or sets the second compound principal-key component.</summary>
        public int Second { get; set; }
    }
}
