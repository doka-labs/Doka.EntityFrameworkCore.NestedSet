using System.Collections.Specialized;
using System.ComponentModel;
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
        Assert.NotNull(contract.PropertyChangingHandler);
        Assert.NotNull(contract.PropertyChangedHandler);
        Assert.NotNull(contract.CollectionChangedHandler);
        Assert.NotNull(contract.FindTrackedEntry);
        Assert.NotNull(contract.SetProperty);
        Assert.NotNull(contract.SetRelationshipSnapshot);
        Assert.NotNull(contract.AddCollectionSnapshot);
        Assert.NotNull(contract.RemoveCollectionSnapshot);
        Assert.NotNull(contract.FindMap);
        Assert.NotNull(contract.Dependents);
        Assert.NotNull(contract.GetOrCreateDependents);
        Assert.NotNull(contract.StopTracking);
        Assert.NotNull(contract.UpdateReferences);
        Assert.NotNull(contract.Setter);
        var cache = Assert.IsType<ConditionalWeakTable<Type, NestedSetInsertionContract.KeyMap>>(
            cacheField.GetValue(contract));

        // WHY: One registration must leave the entire supported map matrix cold, not only one selected cache cell.
        foreach (var definition in definitions)
        {
            var mapDefinition = assembly.GetType(
                "Microsoft.EntityFrameworkCore.ChangeTracking.Internal." + definition)!;

            // WHY: Direct array traversal checks every closed key shape against the same untouched contract cache.
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
        var options = new DbContextOptionsBuilder<SnapshotContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

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

    /// <summary>Snapshot restoration changes neither current CLR relationships nor their ordinary originals.</summary>
    [Fact]
    public async Task RelationshipSnapshotSetterSeparatesStoredAndCurrentValues()
    {
        // Arrange
        await using var context = new RelationshipContext();
        var parent = new RelationshipEntity { Id = 1 };
        var replacement = new RelationshipEntity { Id = 3 };
        var child = new RelationshipEntity
        {
            Id = 2,
            ParentId = 1,
            Parent = parent,
        };

        parent.Children.Add(child);
        context.AttachRange(parent, replacement);
        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Entry(child));
        var parentProperty = update.EntityType.FindProperty(nameof(RelationshipEntity.ParentId))!;
        var parentNavigation = update.EntityType.FindNavigation(nameof(RelationshipEntity.Parent))!;
        child.ParentId = 3;
        child.Parent = replacement;

        // Act
        contract.SetRelationshipSnapshot(update, parentProperty, 7);
        contract.SetRelationshipSnapshot(update, parentNavigation, replacement);

        // Assert
        Assert.Equal(7, update.GetRelationshipSnapshotValue(parentProperty));
        Assert.Same(replacement, update.GetRelationshipSnapshotValue(parentNavigation));
        Assert.Equal(3, child.ParentId);
        Assert.Same(replacement, child.Parent);
        Assert.Equal(1, update.GetOriginalValue<int?>(parentProperty));
        Assert.False(update.IsModified(parentProperty));
        Assert.Equal(EntityState.Unchanged, update.EntityState);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Known detached members enter only the native reference set, without implicit graph tracking.</summary>
    [Fact]
    public async Task CollectionSnapshotAdditionRetainsDistinctReferencesWithoutTrackingThem()
    {
        // Arrange
        await using var context = new RelationshipContext();
        var parent = new RelationshipEntity { Id = 1 };
        context.Attach(parent);
        var first = new RelationshipEntity { Id = 2 };
        var sameKey = new RelationshipEntity { Id = 2 };
        parent.Children.Add(first);
        parent.Children.Add(sameKey);
        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Entry(parent));
        var navigation = update.EntityType.FindNavigation(nameof(RelationshipEntity.Children))!;

        // Act
        contract.AddCollectionSnapshot(update, navigation, first);
        contract.AddCollectionSnapshot(update, navigation, sameKey);
        contract.AddCollectionSnapshot(update, navigation, first);

        // Assert
        var snapshot = Assert.IsAssignableFrom<ISet<object>>(update.GetRelationshipSnapshotValue(navigation));
        Assert.Equal(2, snapshot.Count);
        Assert.True(snapshot.Contains(first));
        Assert.True(snapshot.Contains(sameKey));
        Assert.Collection(
            parent.Children,
            member => Assert.Same(first, member),
            member => Assert.Same(sameKey, member));

        Assert.Same(
            parent,
            Assert.Single(context.ChangeTracker.Entries())
                .Entity);

        Assert.Equal(EntityState.Unchanged, update.EntityState);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Removing a recorded relationship never edits its current collection or tracked entity state.</summary>
    [Fact]
    public async Task CollectionSnapshotRemovalPreservesCurrentMembers()
    {
        // Arrange
        await using var context = new RelationshipContext();
        var parent = new RelationshipEntity { Id = 1 };
        var child = new RelationshipEntity
        {
            Id = 2,
            ParentId = 1,
            Parent = parent,
        };

        parent.Children.Add(child);
        context.Attach(parent);
        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Entry(parent));
        var navigation = update.EntityType.FindNavigation(nameof(RelationshipEntity.Children))!;

        // Act
        contract.RemoveCollectionSnapshot(update, navigation, child);

        // Assert
        Assert.Empty(Assert.IsAssignableFrom<ISet<object>>(update.GetRelationshipSnapshotValue(navigation)));
        Assert.Same(child, Assert.Single(parent.Children));
        Assert.Same(parent, child.Parent);
        Assert.Equal(1, child.ParentId);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Closed handler identity matches EF event subscriptions and never retains another entry.</summary>
    /// <param name="kind">The property or collection notification whose native handler is closed.</param>
    [Theory]
    [InlineData("changing")]
    [InlineData("changed")]
    [InlineData("collection")]
    public async Task NotificationFactoriesCloseExactNativeHandlerIdentity(
        string kind
    )
    {
        // Arrange
        await using var context = new HierarchyChangeDetectionTests.SnapshotContext();
        var contract = NestedSetInsertionContract.Instance;
        var first = contract.Entry(context.Attach(HierarchyChangeDetectionTests.CreateNode()));
        var second = contract.Entry(context.Attach(HierarchyChangeDetectionTests.CreateNode(2)));
        var factory = NotificationFactory(contract, kind);

        // Act
        var handler = factory(first);
        var repeated = factory(first);
        var other = factory(second);

        // Assert
        Assert.Same(first, handler.Target);
        Assert.Same(second, other.Target);
        Assert.Equal(handler, repeated);
        Assert.NotEqual(handler, other);
        Assert.Single(handler.GetInvocationList());
        Assert.Equal(first.GetType(), handler.Method.DeclaringType);
        Assert.Equal(EntityState.Unchanged, first.EntityState);
        Assert.Equal(EntityState.Unchanged, second.EntityState);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Exact property delegates pause EF, retain application callbacks and resume native tracking.</summary>
    [Fact]
    public async Task PropertyHandlersPauseAndResumeWithoutDetachingOrRemovingApplicationListeners()
    {
        // Arrange
        await using var context = new NotificationBindingContext();
        var node = HierarchyChangeDetectionTests.CreateNode();
        node.Payload = "initial";
        var contract = NestedSetInsertionContract.Instance;
        var tracked = context.Attach(node);

        // WHY: Notification originals are lazy. Establish a real original before testing that pausing EF
        // preserves it; otherwise resumption correctly snapshots the current value when its slot is created.
        tracked.Property(value => value.Payload)
            .OriginalValue = "initial";

        var update = contract.Entry(tracked);
        var property = update.EntityType.FindProperty(nameof(HierarchyChangeDetectionTests.ProbeNode.Payload))!;
        var originalBefore = update.GetOriginalValue<string>(property);
        var modifiedBefore = update.IsModified(property);
        var changing = contract.PropertyChangingHandler(update);
        var changed = contract.PropertyChangedHandler(update);
        var applicationChanging = 0;
        var applicationChanged = 0;
        node.PropertyChanging += (_, _) => applicationChanging++;
        node.PropertyChanged += (_, _) => applicationChanged++;

        // Act
        node.PropertyChanging -= changing;
        node.PropertyChanged -= changed;
        node.Payload = "paused";
        var pausedModified = update.IsModified(property);
        var pausedState = update.EntityState;
        node.PropertyChanging += changing;
        node.PropertyChanged += changed;
        node.Payload = "resumed";

        // Assert
        Assert.Equal("initial", originalBefore);
        Assert.False(modifiedBefore);
        Assert.False(pausedModified);
        Assert.Equal(EntityState.Unchanged, pausedState);
        Assert.True(update.IsModified(property));
        Assert.Equal(EntityState.Modified, update.EntityState);
        Assert.Equal("initial", update.GetOriginalValue<string>(property));
        Assert.Equal("resumed", node.Payload);
        Assert.Equal(2, applicationChanging);
        Assert.Equal(2, applicationChanged);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Collection event pause leaves raw members untracked; restoring its handler resumes EF fixup.</summary>
    [Fact]
    public async Task CollectionHandlerPauseAndResumeControlsOnlyNativeNotificationFixup()
    {
        // Arrange
        await using var context = new NotificationBindingContext();
        var parent = HierarchyChangeDetectionTests.CreateNode();
        var child = HierarchyChangeDetectionTests.CreateNode(2);
        var contract = NestedSetInsertionContract.Instance;
        var update = contract.Entry(context.Attach(parent));
        var children = parent.Children!;
        var handler = contract.CollectionChangedHandler(update);
        var applicationEvents = 0;
        children.CollectionChanged += (_, _) => applicationEvents++;

        // Act
        children.CollectionChanged -= handler;
        children.Add(child);
        var pausedEntries = context
            .ChangeTracker
            .Entries()
            .Count();

        children.Remove(child);
        children.CollectionChanged += handler;
        children.Add(child);
        var childState = context.Entry(child)
            .State;

        // Assert
        Assert.Equal(1, pausedEntries);
        Assert.Equal(
            2,
            context
                .ChangeTracker
                .Entries()
                .Count());

        Assert.Equal(EntityState.Added, childState);
        Assert.Same(parent, child.Parent);
        Assert.Equal(parent.Id, child.ParentId);
        Assert.Same(children, parent.Children);
        Assert.Equal(3, applicationEvents);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Notification handlers require exact argument and return types before any model can run.</summary>
    /// <param name="member">The native handler whose signature must remain qualified.</param>
    /// <param name="wrongReturn">Whether the mismatch is the return rather than the event argument.</param>
    [Theory]
    [InlineData("HandleINotifyPropertyChanging", false)]
    [InlineData("HandleINotifyPropertyChanging", true)]
    [InlineData("HandleINotifyPropertyChanged", false)]
    [InlineData("HandleINotifyPropertyChanged", true)]
    [InlineData("HandleINotifyCollectionChanged", false)]
    [InlineData("HandleINotifyCollectionChanged", true)]
    public void NotificationMethodResolutionRejectsInexactSignatures(
        string member,
        bool wrongReturn
    )
    {
        // Arrange
        var assembly = typeof(IUpdateEntry).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var contract = NestedSetInsertionContract.Bind(assembly);
        var native = assembly.GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal.InternalEntityEntry")!;
        var resolve = typeof(NestedSetInsertionContract).GetMethod(
            "RequireMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var arguments = member switch
        {
            "HandleINotifyPropertyChanging" => typeof(PropertyChangingEventArgs),
            "HandleINotifyPropertyChanged" => typeof(PropertyChangedEventArgs),
            "HandleINotifyCollectionChanged" => typeof(NotifyCollectionChangedEventArgs),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };

        Type[] parameters =
        [
            typeof(object), wrongReturn
                ? arguments
                : typeof(EventArgs),
        ];

        var result = wrongReturn
            ? typeof(object)
            : typeof(void);

        // Act
        var error = Record.Exception(() => resolve.Invoke(
            contract,
            BindingFlags.DoNotWrapExceptions,
            binder: null,
            [native, member, result, parameters],
            culture: null));

        // Assert
        var incompatible = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(version, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(member, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(parameters[1].FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(result.FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets", incompatible.Message, StringComparison.Ordinal);
    }

    /// <summary>Adapts test selection while the resulting delegate keeps its native target and method.</summary>
    private static Func<IUpdateEntry, Delegate> NotificationFactory(
        NestedSetInsertionContract contract,
        string kind
    ) => kind switch
    {
        "changing" => entry => contract.PropertyChangingHandler(entry),
        "changed" => entry => contract.PropertyChangedHandler(entry),
        "collection" => entry => contract.CollectionChangedHandler(entry),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Registration rejects narrowed metadata parameters and incorrect returns.</summary>
    /// <param name="member">The native relationship operation whose exact contract is checked.</param>
    /// <param name="wrongReturn">Whether the mismatch is a return type instead of a metadata parameter.</param>
    [Theory]
    [InlineData("SetRelationshipSnapshotValue", false)]
    [InlineData("SetRelationshipSnapshotValue", true)]
    [InlineData("AddToCollectionSnapshot", false)]
    [InlineData("AddToCollectionSnapshot", true)]
    [InlineData("RemoveFromCollectionSnapshot", false)]
    [InlineData("RemoveFromCollectionSnapshot", true)]
    public void RelationshipMethodResolutionRejectsInexactSignatures(
        string member,
        bool wrongReturn
    )
    {
        // Arrange
        var assembly = typeof(IUpdateEntry).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var contract = NestedSetInsertionContract.Bind(assembly);
        var native = assembly.GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal.InternalEntityEntry")!;
        var resolve = typeof(NestedSetInsertionContract).GetMethod(
            "RequireMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var scalar = member == "SetRelationshipSnapshotValue";
        var expectedMetadata = scalar
            ? typeof(IPropertyBase)
            : typeof(INavigationBase);

        Type[] parameters =
        [
            wrongReturn
                ? expectedMetadata
                : scalar
                    ? typeof(IProperty)
                    : typeof(INavigation),
            typeof(object),
        ];

        var result = wrongReturn
            ? typeof(object)
            : typeof(void);

        // Act
        var error = Record.Exception(() => resolve.Invoke(
            contract,
            BindingFlags.DoNotWrapExceptions,
            binder: null,
            [native, member, result, parameters],
            culture: null));

        // Assert
        var incompatible = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(version, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(member, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(parameters[0].FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(result.FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets", incompatible.Message, StringComparison.Ordinal);
    }

    /// <summary>Dependent lookup requires the exact key-vector, FK metadata and return types.</summary>
    /// <param name="wrongReturn">Whether the mismatch is the return instead of the FK metadata.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependentLookupRejectsInexactSignatures(
        bool wrongReturn
    )
    {
        // Arrange
        var assembly = typeof(IUpdateEntry).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var contract = NestedSetInsertionContract.Bind(assembly);
        var type = assembly.GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager")!;
        var resolve = typeof(NestedSetInsertionContract).GetMethod(
            "RequireMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Type[] parameters =
        [
            typeof(IReadOnlyList<object>), wrongReturn
                ? typeof(IForeignKey)
                : typeof(IReadOnlyForeignKey),
        ];

        var result = wrongReturn
            ? typeof(object)
            : typeof(IEnumerable<IUpdateEntry>);

        // Act
        var error = Record.Exception(() => resolve.Invoke(
            contract,
            BindingFlags.DoNotWrapExceptions,
            binder: null,
            [type, "GetDependents", result, parameters],
            culture: null));

        // Assert
        var incompatible = Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(version, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("GetDependents", incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(parameters[^1].FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains(result.FullName!, incompatible.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets", incompatible.Message, StringComparison.Ordinal);
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

    /// <summary>The typed native callback validates its framework seam before any context or model exists.</summary>
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

            var adapterAssembly = loadContext.LoadFromAssemblyPath(
                typeof(SqliteNestedSetOptionsBuilderExtensions).Assembly.Location);

            var registrationType = adapterAssembly.GetType(
                typeof(SqliteNestedSetOptionsBuilderExtensions).FullName!,
                throwOnError: true)!;

            var registration = registrationType.GetMethod(
                nameof(SqliteNestedSetOptionsBuilderExtensions.UseNestedSets),
                [typeof(SqliteDbContextOptionsBuilder)])!;

            var options = new DbContextOptionsBuilder();
            var native = new SqliteDbContextOptionsBuilder(options);
            var initiallyCreated = (bool)created.GetValue(lazy)!;

            // Act
            var registered = registration.Invoke(null, [native]);

            // Assert
            Assert.False(initiallyCreated);
            Assert.Same(native, registered);
            Assert.True((bool)created.GetValue(lazy)!);
            Assert.False(
                Assert.Single(options.Options.Extensions)
                    .Info.IsDatabaseProvider);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    /// <summary>Uses the shared real-notification model with originals retained for the pause/resume control.</summary>
    private sealed class NotificationBindingContext : HierarchyChangeDetectionTests.ProbeContext
    {
        /// <inheritdoc />
        protected override ChangeTrackingStrategy TrackingStrategy =>
            ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues;
    }

    /// <summary>Supplies real scalar, reference and collection snapshot slots without I/O.</summary>
    private sealed class RelationshipContext : DbContext
    {
        /// <summary>Keeps deliberately raw navigation changes separate from native snapshot writes.</summary>
        internal RelationshipContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<RelationshipEntity>();
            entity
                .Property(node => node.Id)
                .ValueGeneratedNever();

            entity
                .HasOne(node => node.Parent)
                .WithMany(node => node.Children)
                .HasForeignKey(node => node.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Collides in domain equality while native snapshot membership remains by reference.</summary>
    private sealed class RelationshipEntity
    {
        /// <summary>Gets or sets the explicitly assigned key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional ordinary self-reference key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the ordinary reference whose snapshot is independent of its current value.</summary>
        public RelationshipEntity? Parent { get; set; }

        /// <summary>Gets the current list, whose duplicate references are deduplicated in native snapshots.</summary>
        public List<RelationshipEntity> Children { get; } = [];

        /// <inheritdoc />
        public override bool Equals(
            object? other
        ) => other is RelationshipEntity entity && entity.Id == Id;

        /// <inheritdoc />
        public override int GetHashCode() => Id;
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
