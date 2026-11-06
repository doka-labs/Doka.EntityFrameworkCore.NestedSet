using System.Collections.Specialized;
using System.ComponentModel;

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Pauses only EF's handlers while rollback restores a captured notification graph.</summary>
internal sealed class NestedSetNotificationPause : IDisposable
{
    private readonly INotifyPropertyChanged? _changed;
    private readonly PropertyChangedEventHandler _changedHandler;
    private readonly INotifyPropertyChanging? _changing;
    private readonly PropertyChangingEventHandler _changingHandler;
    private readonly NotifyCollectionChangedEventHandler _collectionHandler;
    private readonly IUpdateEntry _entry;
    private readonly INavigationBase _navigation;
    private bool _disposed;

    /// <summary>Removes exact native delegates while leaving every application listener installed.</summary>
    private NestedSetNotificationPause(
        IUpdateEntry entry,
        INavigationBase navigation,
        object? originalCollection
    )
    {
        _entry = entry;
        _navigation = navigation;
        var contract = NestedSetInsertionContract.Instance;
        _changingHandler = contract.PropertyChangingHandler(entry);
        _changedHandler = contract.PropertyChangedHandler(entry);
        _collectionHandler = contract.CollectionChangedHandler(entry);
        var entity = entry.ToEntityEntry()
            .Entity;

        var strategy = entry.EntityType.GetChangeTrackingStrategy();
        _changing = strategy is ChangeTrackingStrategy.ChangingAndChangedNotifications
            or ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues
            ? entity as INotifyPropertyChanging
            : null;

        _changed = entity as INotifyPropertyChanged;
        _changing?.PropertyChanging -= _changingHandler;
        _changed?.PropertyChanged -= _changedHandler;

        if (navigation.IsCollection)
        {
            var current = entry.GetCurrentValue(navigation);

            if (current is INotifyCollectionChanged currentEvents)
            {
                currentEvents.CollectionChanged -= _collectionHandler;
            }

            if (!ReferenceEquals(current, originalCollection)
                && originalCollection is INotifyCollectionChanged original)
            {
                // WHY: ChangedNotifications has no PropertyChanging hook to unsubscribe a replaced old collection.
                // Restore its saved instance with EF paused too, instead of accidentally reattaching detached inputs.
                original.CollectionChanged -= _collectionHandler;
            }
        }
    }

    /// <summary>Restores EF subscriptions on the actual final graph, including literal null collections.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_entry.EntityState != EntityState.Detached
                && _navigation.IsCollection
                && _entry.GetCurrentValue(_navigation) is INotifyCollectionChanged current)
            {
                // WHY: Subscribing directly preserves a captured null collection. The framework subscriber's
                // GetOrCreate path would initialize it and change the graph that rollback just restored.
                current.CollectionChanged += _collectionHandler;
            }
        }
        finally
        {
            if (_entry.EntityState != EntityState.Detached)
            {
                _changing?.PropertyChanging += _changingHandler;
                _changed?.PropertyChanged += _changedHandler;
            }

            _disposed = true;
        }
    }

    /// <summary>Pauses a tracked notification owner without initializing null collection storage.</summary>
    /// <param name="entry">The exact tracked owner whose captured graph is being restored.</param>
    /// <param name="navigation">The reference or collection whose CLR storage will be restored.</param>
    /// <param name="originalCollection">The captured collection object, or null for absent/reference storage.</param>
    /// <returns>A recovery-only scope, or null when no EF notifications are subscribed.</returns>
    internal static NestedSetNotificationPause? Begin(
        IUpdateEntry entry,
        INavigationBase navigation,
        object? originalCollection
    ) => entry.EntityState == EntityState.Detached
        || entry.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot
            ? null
            : new NestedSetNotificationPause(entry, navigation, originalCollection);
}
