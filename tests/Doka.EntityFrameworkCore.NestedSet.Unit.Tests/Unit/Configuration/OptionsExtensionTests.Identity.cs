using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class OptionsExtensionTests
{
    /// <summary>Equal assembly and type names in another load context do not share an EF service identity.</summary>
    [Fact]
    public void EqualAdapterNamesFromAnotherLoadContextRemainDifferentOwners()
    {
        // Arrange
        using var foreign = ForeignAdapter.Load();
        var selected = NestedSetTestAdapters.ForEngine("MySql");
        var first = ((IDbContextOptionsExtension)new NestedSetOptionsExtension(selected)).Info;
        var second = ((IDbContextOptionsExtension)new NestedSetOptionsExtension(foreign.Provider)).Info;

        // Act
        var same = first.ShouldUseSameServiceProvider(second);

        // Assert
        Assert.Equal(selected.GetType().FullName, foreign.Provider.GetType().FullName);
        Assert.Equal(selected.GetType().Assembly.FullName, foreign.Provider.GetType().Assembly.FullName);
        Assert.NotSame(selected.GetType().Assembly, foreign.Provider.GetType().Assembly);
        Assert.False(same);
        Assert.False(second.ShouldUseSameServiceProvider(first));
    }

    /// <summary>A same-name adapter in another runtime assembly cannot replace the selected external service.</summary>
    /// <param name="suppliedModel">Whether model conventions are bypassed by an initialized runtime model.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignLoadContextAdapterIsRejectedBeforeModelOrIo(bool suppliedModel)
    {
        // Arrange
        using var foreign = ForeignAdapter.Load();
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<InitializationProbeContext>(
                ModelCompatibilityDatabase.Options<InitializationProbeContext>("MySql", Connection("MySql")))
            .AddInterceptors(boundary, boundary.Commands).Options;

        using var modelServices = OwnedModelTestServices<InitializationProbeContext>.Create(options);
        using var modelContext = new InitializationProbeContext(modelServices.Options);
        var model = suppliedModel ? modelContext.Model : null;
        using var provider = NestedSetTestAdapters.BuildServices(options, services =>
        {
            services.RemoveAll<NestedSetProviderCapabilities>();
            services.AddSingleton(foreign.Provider);
        });

        var builder = new DbContextOptionsBuilder<InitializationProbeContext>(options).UseInternalServiceProvider(provider);

        if (model is not null)
        {
            builder.UseModel(model);
        }

        using var modelBuilds = InitializationProbeContext.ObserveModelCreation();

        // Act
        var error = Record.Exception(() =>
        {
            using var context = new InitializationProbeContext(builder.Options);
            _ = context.Model;
        });

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("external EF service provider", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, modelBuilds.Count);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>The immutable registration does not globally retain a collectible adapter or its options.</summary>
    [Fact]
    public void RegistrationDoesNotRetainACollectibleAdapter()
    {
        // Arrange
        var released = RegisterAndReleaseForeignAdapter();

        // Act
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Assert
        Assert.All(released, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>A deliberately live registration keeps its adapter reachable, validating the lifetime probe.</summary>
    [Fact]
    public void LiveRegistrationRetainsItsCollectibleAdapter()
    {
        // Arrange
        var registration = RegisterForeignAdapter(out var retained);

        // Act
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Assert
        Assert.All(retained, reference => Assert.True(reference.IsAlive));
        GC.KeepAlive(registration);
    }

    /// <summary>Exact native assembly identity is checked even when a foreign provider has the same name.</summary>
    [Fact]
    public void EqualNativeAssemblyNamesCannotSatisfyAdapterValidation()
    {
        // Arrange
        var selected = NestedSetTestAdapters.ForEngine("MySql");
        var actual = ModelCompatibilityDatabase.Options<DbContext>("MySql", Connection("MySql"))
            .Extensions.Single(extension => extension.Info.IsDatabaseProvider);

        var loadContext = new AssemblyLoadContext(nameof(EqualNativeAssemblyNamesCannotSatisfyAdapterValidation),
            isCollectible: true);

        loadContext.Resolving += ResolveSharedDependency;

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(selected.ProviderAssembly.Location);
            var extensionType = assembly.GetType(actual.GetType().FullName!, throwOnError: true)!;
            var foreign = (IDbContextOptionsExtension)Activator.CreateInstance(extensionType, nonPublic: true)!;
            var builder = new DbContextOptionsBuilder();
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(foreign);

            // Act
            var error = Record.Exception(() => selected.ValidateOptions(builder.Options));

            // Assert
            Assert.Equal(selected.ProviderAssembly.FullName, assembly.FullName);
            Assert.NotSame(selected.ProviderAssembly, assembly);
            Assert.IsType<InvalidOperationException>(error);
            Assert.Contains("actual native provider assembly", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    /// <summary>Releases all strong registration inputs outside the caller's collection window.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RegisterAndReleaseForeignAdapter()
    {
        _ = RegisterForeignAdapter(out var released);

        return released;
    }

    /// <summary>Creates the same registration for live-root and released-root controls.</summary>
    /// <param name="references">Weak observers of the adapter, load context, builder, and extension.</param>
    /// <returns>The sole deliberately retained registration root for the negative control.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DbContextOptionsBuilder RegisterForeignAdapter(out WeakReference[] references)
    {
        using var foreign = ForeignAdapter.Load();
        var builder = new DbContextOptionsBuilder();
        NestedSetProviderRegistration.Register(builder, foreign.Provider);
        var extension = builder.Options.FindExtension<NestedSetOptionsExtension>()!;
        var information = ((IDbContextOptionsExtension)extension).Info;
        _ = information.GetServiceProviderHashCode();

        references = [new(foreign.Provider), new(foreign.Context), new(builder), new(extension)];

        return builder;
    }

    /// <summary>Shares reviewed native/framework dependencies while the explicitly loaded adapter stays isolated.</summary>
    private static Assembly? ResolveSharedDependency(AssemblyLoadContext context, AssemblyName name)
        => AssemblyLoadContext.Default.Assemblies.SingleOrDefault(assembly =>
            AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), name));

    /// <summary>Owns one genuine adapter assembly loaded separately from the default runtime identity.</summary>
    private sealed class ForeignAdapter : IDisposable
    {
        /// <summary>Gets the collectible assembly owner.</summary>
        internal AssemblyLoadContext Context { get; }

        /// <summary>Gets the actual stateless adapter from the isolated module.</summary>
        internal NestedSetProviderCapabilities Provider { get; }

        /// <summary>Captures the loaded module without installing it into a process-global EF service cache.</summary>
        private ForeignAdapter(AssemblyLoadContext context, NestedSetProviderCapabilities provider)
        {
            Context = context;
            Provider = provider;
        }

        /// <summary>Loads the shipping MySql adapter while sharing its common/native declared dependencies.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static ForeignAdapter Load()
        {
            var selected = NestedSetTestAdapters.ForEngine("MySql");
            var context = new AssemblyLoadContext("NestedSet.AdapterOwnership." + Guid.NewGuid().ToString("N"),
                isCollectible: true);

            context.Resolving += ResolveSharedDependency;
            var assembly = context.LoadFromAssemblyPath(selected.GetType().Assembly.Location);
            var type = assembly.GetType(selected.GetType().FullName!, throwOnError: true)!;
            var instance = type.GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

            return new ForeignAdapter(context, (NestedSetProviderCapabilities)instance);
        }

        /// <inheritdoc />
        public void Dispose() => Context.Unload();
    }
}
