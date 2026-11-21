using System.Collections;
using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects model-owned factories when separate contexts plan their first saves concurrently.</summary>
public sealed class FactoryCacheConcurrencyTests
{
    /// <summary>Opposite owner orders share one cache, including when an individual plan is rejected.</summary>
    /// <param name="rejectSomePlans">Whether some contexts explicitly attempt a forbidden root move.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_ConcurrentSharedModel_PublishesOneFactoryPerOwner(
        bool rejectSomePlans
    )
    {
        // Arrange
        var template = ConfigureOptions().Options;
        await using var services = NestedSetTestAdapters.BuildServices(
            template,
            registrations => registrations.Replace(
                ServiceDescriptor.Singleton<IModelCacheKeyFactory, TestModelCacheKeyFactory>()));

        var options = new DbContextOptionsBuilder<FactoryContext>(template)
            .UseInternalServiceProvider(services)
            .Options;

        var variant = Guid.NewGuid();
        await using var seed = new FactoryContext(options, variant);
        var model = seed.Model;
        var before = CacheFor(model);
        var sharedOptions = new DbContextOptionsBuilder<FactoryContext>(options)
            .UseModel(model)
            .Options;

        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var observations = await CreateConcurrentlyAsync(sharedOptions, variant, rejectSomePlans, cancellationToken);
        var cache = CacheFor(model);
        var factories = Factories(cache!);

        // Assert
        Assert.Null(before);
        Assert.NotNull(cache);
        Assert.Equal(2, factories.Count);
        Assert.True(factories.Contains(model.FindEntityType(typeof(CacheNode<FirstOwner>))!));
        Assert.True(factories.Contains(model.FindEntityType(typeof(CacheNode<SecondOwner>))!));
        Assert.All(observations, observation =>
        {
            Assert.Same(cache, observation.Cache);
            Assert.False(observation.AutomaticDetection);
            Assert.Equal(ConnectionState.Closed, observation.ConnectionState);

            if (observation.Rejected)
            {
                var failure = Assert.IsType<NestedSetException>(observation.Failure);
                Assert.Equal(NestedSetErrorCode.OperationRejected, failure.Code);
                Assert.Empty(observation.Owners);
            }
            else
            {
                Assert.Null(observation.Failure);
                Assert.Equal(2, observation.Owners.Length);
                Assert.Contains(typeof(CacheNode<FirstOwner>), observation.Owners);
                Assert.Contains(typeof(CacheNode<SecondOwner>), observation.Owners);
            }
        });

        Assert.All(factories.Values.Cast<Delegate>(), factory =>
        {
            // WHY: A cached static factory must never capture any of the contexts that raced to publish it.
            Assert.Null(factory.Target);
            Assert.Equal(typeof(NestedSetSaveGroup), factory.Method.DeclaringType);
        });
    }

    /// <summary>Releases separate contexts into the same model's first-use publication boundary.</summary>
    private static async Task<Observation[]> CreateConcurrentlyAsync(
        DbContextOptions<FactoryContext> options,
        Guid variant,
        bool rejectSomePlans,
        CancellationToken cancellationToken
    )
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = Enumerable.Range(0, 32)
            .Select(index => ObserveAfterStartAsync(
                start.Task,
                options,
                variant,
                index,
                rejectSomePlans && index % 4 == 0,
                cancellationToken))
            .ToArray();

        // WHY: Every observer has reached this async gate before release. Separate contexts can then race on
        // ThreadPool continuations without blocking workers or depending on a scheduling-time threshold.
        start.SetResult();

        return await Task.WhenAll(work);
    }

    /// <summary>Waits until every observer is registered before entering the shared first-use boundary.</summary>
    private static async Task<Observation> ObserveAfterStartAsync(
        Task start,
        DbContextOptions<FactoryContext> options,
        Guid variant,
        int index,
        bool reject,
        CancellationToken cancellationToken
    )
    {
        // WHY: Do not capture xUnit's synchronization context; the planners must use independent continuations.
        await start.WaitAsync(cancellationToken).ConfigureAwait(false);

        return Observe(options, variant, index, reject, cancellationToken);
    }

    /// <summary>Observes one planner without treating its rejected plan as a successful result.</summary>
    private static Observation Observe(
        DbContextOptions<FactoryContext> options,
        Guid variant,
        int index,
        bool reject,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var context = new FactoryContext(options, variant);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var first = context.Attach(new CacheNode<FirstOwner> { Id = index + 1 });
        var second = context.Attach(new CacheNode<SecondOwner> { Id = index + 1 });

        if (reject)
        {
            first.Property(node => node.ParentId).IsModified = true;
        }

        EntityEntry[] entries = index % 2 == 0
            ? [first, second]
            : [second, first];

        Type[] owners = [];
        Exception? failure = null;

        try
        {
            owners = NestedSetSaveGroup.Create(context, entries)
                .Select(group => group.GetType().GenericTypeArguments[0])
                .ToArray();
        }
        catch (Exception error)
        {
            failure = error;
        }

        return new Observation(
            reject,
            owners,
            failure,
            CacheFor(context.Model),
            context.ChangeTracker.AutoDetectChangesEnabled,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Reads the weak-cache seam without changing publication or adding a production test hook.</summary>
    private static object? CacheFor(
        IModel model
    )
    {
        var caches = typeof(NestedSetSaveGroup)
            .GetField("s_models", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        object?[] arguments = [model, null];
        var present = (bool)caches.GetType().GetMethod("TryGetValue")!.Invoke(caches, arguments)!;

        return present
            ? arguments[1]
            : null;
    }

    /// <summary>Reads factory identities only after every publisher has completed.</summary>
    private static IDictionary Factories(
        object cache
    ) => (IDictionary)cache.GetType()
        .GetProperty("Factories", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(cache)!;

    /// <summary>Configures a native adapter without opening a database connection.</summary>
    private static DbContextOptionsBuilder<FactoryContext> ConfigureOptions()
        => new DbContextOptionsBuilder<FactoryContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());

    /// <summary>Retains completed observations without retaining disposed contexts.</summary>
    private readonly record struct Observation(
        bool Rejected,
        Type[] Owners,
        Exception? Failure,
        object? Cache,
        bool AutomaticDetection,
        ConnectionState ConnectionState
    );

    /// <summary>Owns one deliberately fresh model shared by all contexts in a concurrency wave.</summary>
    private sealed class FactoryContext : DbContext, ITestModelVariant
    {
        /// <summary>Uses caller-owned services and one model identity for this concurrency wave.</summary>
        /// <param name="options">The options shared without sharing a live context.</param>
        /// <param name="modelVariant">The fresh identity separating this wave from earlier cached models.</param>
        internal FactoryContext(
            DbContextOptions<FactoryContext> options,
            Guid modelVariant
        ) : base(options)
        {
            ModelVariant = modelVariant;
        }

        /// <inheritdoc />
        public object ModelVariant { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigureOwner<FirstOwner>(modelBuilder);
            ConfigureOwner<SecondOwner>(modelBuilder);
        }

        /// <summary>Maps independent owners with the same structural key types.</summary>
        /// <typeparam name="TOwner">The marker identifying this independent hierarchy owner.</typeparam>
        private static void ConfigureOwner<TOwner>(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<CacheNode<TOwner>>();
            node.Property(value => value.Id).ValueGeneratedNever();
            node.HasNestedSet(tree => tree.HasParent(value => value.ParentId));
        }
    }

    /// <summary>Distinguishes the first configured owner without introducing inheritance or shared mappings.</summary>
    private sealed class FirstOwner;

    /// <summary>Distinguishes the second independently configured owner.</summary>
    private sealed class SecondOwner;

    /// <summary>Uses its marker only to establish two exact EF owner identities with the same scalar layout.</summary>
    /// <typeparam name="TOwner">The identity marker separating otherwise identical hierarchy mappings.</typeparam>
    private sealed class CacheNode<TOwner> : INestedSetNode<int, int>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public int TreeId { get; set; } = 1;

        /// <inheritdoc />
        public long Left { get; set; } = 1;

        /// <inheritdoc />
        public long Right { get; set; } = 2;

        /// <summary>Gets or sets the parent link used to exercise explicit root-move rejection.</summary>
        public int? ParentId { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }
}
