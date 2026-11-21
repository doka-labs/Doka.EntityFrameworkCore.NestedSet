using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CollectionBaselineOwnershipTests
{
    /// <summary>Uses real ownership and ordinary relationships without opening a database connection.</summary>
    private sealed class BaselineContext : DbContext
    {
        /// <summary>Keeps payload entry creation detached until the test explicitly starts tracking.</summary>
        internal BaselineContext()
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
            modelBuilder
                .Entity<ExternalOwner>()
                .Property(value => value.Id)
                .ValueGeneratedNever();

            var dependent = modelBuilder.Entity<ExternalDependent>();
            dependent
                .Property(value => value.Id)
                .ValueGeneratedNever();

            dependent
                .HasOne(value => value.ExternalOwner)
                .WithMany(value => value.Dependents)
                .HasForeignKey(value => value.ExternalOwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            var root = modelBuilder.Entity<OwnedRoot>();
            root
                .Property(value => value.Id)
                .ValueGeneratedNever();

            root.OwnsMany(
                value => value.Payloads,
                payload =>
                {
                    payload
                        .WithOwner(value => value.Root)
                        .HasForeignKey(value => value.RootId);

                    payload.HasKey(value => new
                    {
                        value.RootId,
                        value.Id,
                    });

                    payload
                        .Property(value => value.Id)
                        .ValueGeneratedNever();
                });
        }
    }

    /// <summary>Uses a notification owner with an ordinary, independently detachable child mapping.</summary>
    private class NotificationBaselineContext : DbContext
    {
        /// <summary>Retains caller-controlled detection during collection cleanup.</summary>
        internal NotificationBaselineContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <summary>Gets the notification strategy whose subscription lifetime is exercised.</summary>
        protected virtual ChangeTrackingStrategy TrackingStrategy =>
            ChangeTrackingStrategy.ChangingAndChangedNotifications;

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var owner = modelBuilder.Entity<NotificationOwner>();
            owner.HasChangeTrackingStrategy(TrackingStrategy);
            owner
                .Property(value => value.Id)
                .ValueGeneratedNever();

            var dependent = modelBuilder.Entity<NotificationDependent>();
            dependent
                .Property(value => value.Id)
                .ValueGeneratedNever();

            dependent
                .HasOne(value => value.Owner)
                .WithMany(value => value.Dependents)
                .HasForeignKey(value => value.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Retains ordinary originals while using only changed notifications for fixup.</summary>
    private sealed class ChangedNotificationBaselineContext : NotificationBaselineContext
    {
        /// <inheritdoc />
        protected override ChangeTrackingStrategy TrackingStrategy => ChangeTrackingStrategy.ChangedNotifications;
    }

    /// <summary>Retains scalar originals while subscribing to both notification stages.</summary>
    private sealed class OriginalValueNotificationBaselineContext : NotificationBaselineContext
    {
        /// <inheritdoc />
        protected override ChangeTrackingStrategy TrackingStrategy =>
            ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues;
    }

    /// <summary>Owns payload rows introduced with one import root.</summary>
    private sealed class OwnedRoot
    {
        /// <summary>Gets or sets the fixed root key used before any provider-generated values exist.</summary>
        public int Id { get; set; }

        /// <summary>Gets the import-owned payload rows.</summary>
        public List<OwnedPayload> Payloads { get; } = [];
    }

    /// <summary>Represents an unrelated external collection owner that may already be persisted.</summary>
    private sealed class ExternalOwner
    {
        /// <summary>Gets or sets the external owner's fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets the ordinary inverse collection whose saved baseline requires protection.</summary>
        public List<ExternalDependent> Dependents { get; } = [];
    }

    /// <summary>Belongs only to its import root through EF ownership.</summary>
    private sealed class OwnedPayload
    {
        /// <summary>Gets or sets the local key within its root.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the ownership FK available while this entry is still Detached.</summary>
        public int RootId { get; set; }

        /// <summary>Gets or sets the ownership navigation.</summary>
        public required OwnedRoot Root { get; set; }
    }

    /// <summary>References an independently tracked principal through an ordinary, non-ownership FK.</summary>
    private sealed class ExternalDependent
    {
        /// <summary>Gets or sets the fixed dependent identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the ordinary external-owner FK before this input is tracked.</summary>
        public int ExternalOwnerId { get; set; }

        /// <summary>Gets or sets the ordinary reference, which may be null before detached-input fixup.</summary>
        public ExternalOwner? ExternalOwner { get; set; }
    }

    /// <summary>Exposes notification collections without EF collection relationship snapshots.</summary>
    private sealed class NotificationOwner : INotifyPropertyChanging, INotifyPropertyChanged
    {
        private NotificationDependents? _dependents = new();
        private int _id;
        private string _payload = string.Empty;

        /// <summary>Gets or sets the fixed owner key.</summary>
        public int Id
        {
            get => _id;
            set => Set(ref _id, value, nameof(Id));
        }

        /// <summary>Gets or sets ordinary pending application data whose modified flag must survive cleanup.</summary>
        public string Payload
        {
            get => _payload;
            set => Set(ref _payload, value, nameof(Payload));
        }

        /// <summary>Gets the collection whose exact reference and known detached member predate the callback.</summary>
        public NotificationDependents? Dependents
        {
            get => _dependents;
            set => Set(ref _dependents, value, nameof(Dependents));
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <inheritdoc />
        public event PropertyChangingEventHandler? PropertyChanging;

        /// <summary>Raises EF's actual notification pair around one scalar change.</summary>
        private void Set<TValue>(
            ref TValue field,
            TValue value,
            string propertyName
        )
        {
            if (EqualityComparer<TValue>.Default.Equals(field, value))
            {
                return;
            }

            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Models a known import reference detached from its prior ordinary owner before capture.</summary>
    private sealed class NotificationDependent
    {
        /// <summary>Gets or sets the stable dependent key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional current FK independently of the stale prepared collection.</summary>
        public int? OwnerId { get; set; }

        /// <summary>Gets or sets the current ordinary owner reference.</summary>
        public NotificationOwner? Owner { get; set; }
    }

    /// <summary>Observes only this test collection's native EF subscriptions without private EF inspection.</summary>
    private sealed class NotificationDependents : ObservableCollection<NotificationDependent>
    {
        private NotifyCollectionChangedEventHandler? _changed;

        /// <summary>Allows EF's real collection accessor to instantiate a missing optional collection.</summary>
        public NotificationDependents() { }

        /// <summary>Gets the native EF handlers currently attached to this collection.</summary>
        internal int NativeHandlerCount =>
            _changed
                ?.GetInvocationList()
                .Count(handler => handler.Target is IUpdateEntry)
            ?? 0;

        /// <inheritdoc />
        public override event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        /// <inheritdoc />
        protected override void OnCollectionChanged(
            NotifyCollectionChangedEventArgs e
        )
        {
            base.OnCollectionChanged(e);
            _changed?.Invoke(this, e);
        }
    }
}
