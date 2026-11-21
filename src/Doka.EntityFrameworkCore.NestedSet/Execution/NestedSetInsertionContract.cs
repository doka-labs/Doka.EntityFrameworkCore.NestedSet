using System.Collections.Specialized;
using System.ComponentModel;

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Binds the model-free EF insertion seams before options registration can complete.</summary>
internal sealed partial class NestedSetInsertionContract
{
    private static readonly Lazy<NestedSetInsertionContract>
        s_instance = new(() => Bind(typeof(IUpdateEntry).Assembly));

    private readonly Assembly _assembly;
    private readonly ConditionalWeakTable<Type, KeyMap>.CreateValueCallback _createMap;
    private readonly Type _entryType;
    private readonly ConditionalWeakTable<Type, KeyMap> _maps = new();
    private readonly MethodInfo _relationshipSnapshot;

    /// <summary>Resolves the loaded framework once and validates declared returns before delegate adaptation.</summary>
    private NestedSetInsertionContract(
        Assembly assembly
    )
    {
        _assembly = assembly;
        _createMap = CreateKeyMap;
        _entryType = NativeType("InternalEntityEntry");
        _ = RequireValueMethod(typeof(IUpdateEntry), nameof(IUpdateEntry.GetCurrentValue));
        _relationshipSnapshot = RequireValueMethod(_entryType, "GetRelationshipSnapshotValue");

        var managerType = NativeType("StateManager");
        var infrastructure = typeof(IInfrastructure<>).MakeGenericType(_entryType);
        var entryGetter = RequireMethod(infrastructure, "get_Instance", _entryType, []);
        var managerGetter = RequireMethod(_entryType, "get_StateManager", NativeType("IStateManager"), []);
        var entry = Expression.Parameter(typeof(EntityEntry), "entry");
        var update = Expression.Parameter(typeof(IUpdateEntry), "update");
        var manager = Expression.Parameter(typeof(object), "manager");
        var property = Expression.Parameter(typeof(IProperty), "property");
        var value = Expression.Parameter(typeof(object), "value");
        var nativeEntry = Expression.Convert(update, _entryType);
        var nativeManager = Expression.Convert(manager, managerType);

        Entry = BindEntry(infrastructure, entryGetter, entry);

        Manager = BindManager(managerGetter, update, nativeEntry);

        (PropertyChangingHandler, PropertyChangedHandler, CollectionChangedHandler) = BindNotifications();

        FindTrackedEntry = BindTrackedEntry(managerType, manager, nativeManager);

        SetProperty = BindPropertySetter(update, property, value, nativeEntry);

        SetRelationshipSnapshot = BindRelationshipSnapshot(update, value, nativeEntry);

        (AddCollectionSnapshot, RemoveCollectionSnapshot) = BindCollectionSnapshots(update, value, nativeEntry);

        FindMap = BindIdentityMap(managerType, manager, nativeManager);

        (Dependents, GetOrCreateDependents, UpdateDependentMap) =
            BindDependentMaps(managerType, update, manager, nativeEntry, nativeManager);

        (GetChangedCount, SetChangedCount) = BindChangedCount(managerType, manager, nativeManager);

        (StopTracking, UpdateReferences) =
            BindTrackingLifecycle(managerType, update, manager, nativeEntry, nativeManager);

        Setter = BindClrSetter();

        ValidateKeyMapSignatures();
    }

    /// <summary>Gets the once-validated contract without retaining any application context or model.</summary>
    internal static NestedSetInsertionContract Instance => s_instance.Value;

    /// <summary>Reads EF's invariant entry identity through its exact infrastructure interface.</summary>
    internal Func<EntityEntry, IUpdateEntry> Entry { get; }

    /// <summary>Reads the context-local manager belonging to an introduced entry.</summary>
    internal Func<IUpdateEntry, object> Manager { get; }

    /// <summary>Closes EF's exact changing handler for rollback-only notification pause and resumption.</summary>
    internal Func<IUpdateEntry, PropertyChangingEventHandler> PropertyChangingHandler { get; }

    /// <summary>Closes EF's exact changed handler without capturing a context in the binding owner.</summary>
    internal Func<IUpdateEntry, PropertyChangedEventHandler> PropertyChangedHandler { get; }

    /// <summary>Closes EF's exact collection handler without creating or replacing a collection.</summary>
    internal Func<IUpdateEntry, NotifyCollectionChangedEventHandler> CollectionChangedHandler { get; }

    /// <summary>Reads an exact reference registration without detecting changes or creating entries.</summary>
    internal Func<object, object, IEntityType, IUpdateEntry?> FindTrackedEntry { get; }

    /// <summary>Restores a shadow key through EF's setter with setModified disabled.</summary>
    internal Action<IUpdateEntry, IProperty, object?> SetProperty { get; }

    /// <summary>Restores a scalar or reference relationship snapshot without changing its live value.</summary>
    internal Action<IUpdateEntry, IPropertyBase, object?> SetRelationshipSnapshot { get; }

    /// <summary>Records one known collection relationship without attaching or adding the live object.</summary>
    internal Action<IUpdateEntry, INavigationBase, object> AddCollectionSnapshot { get; }

    /// <summary>Removes one member from an existing collection snapshot without changing its live collection.</summary>
    internal Action<IUpdateEntry, INavigationBase, object> RemoveCollectionSnapshot { get; }

    /// <summary>Finds an existing native identity map without creating one.</summary>
    internal Func<object, IKey, object?> FindMap { get; }

    /// <summary>Reads an exact native foreign-key bucket through its public update-entry result.</summary>
    internal Func<object, IReadOnlyList<object?>, IEnumerable<IUpdateEntry>> Dependents { get; }

    /// <summary>Reads actual FK membership, letting EF initialize its own lazy dependent map when needed.</summary>
    internal Func<object, IReadOnlyList<object?>, IForeignKey, IEnumerable<IUpdateEntry>> GetOrCreateDependents { get; }

    /// <summary>Re-indexes one tracked dependent through EF's map when a lazy map missed its reattachment.</summary>
    internal Action<object, IUpdateEntry, IForeignKey> UpdateDependentMap { get; }

    /// <summary>Reads EF's count of pending entries, which gates whether a save does any work.</summary>
    internal Func<object, int> GetChangedCount { get; }

    /// <summary>Corrects EF's pending count after a transition of an entry that Clear already uncounted.</summary>
    internal Action<object, int> SetChangedCount { get; }

    /// <summary>Removes a partially installed entry through EF's normal identity cleanup.</summary>
    internal Action<object, IUpdateEntry, EntityState> StopTracking { get; }

    /// <summary>Removes exact partial reference registrations without a fabricated state transition.</summary>
    internal Action<object, IUpdateEntry, EntityState, EntityState?> UpdateReferences { get; }

    /// <summary>Gets EF's metadata-owned setter without duplicating complex value copyback.</summary>
    internal Func<IPropertyBase, IClrPropertySetter> Setter { get; }
}
