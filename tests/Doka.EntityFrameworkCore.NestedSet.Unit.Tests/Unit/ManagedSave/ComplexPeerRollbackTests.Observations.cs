using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexPeerRollbackTests
{
    /// <summary>Tracks a valid loaded graph with stored originals before any raw complex mutation.</summary>
    private static EntityEntry<ComplexPeer> Track(
        PeerContext context
    )
    {
        var peer = new ComplexPeer
        {
            Id = 7,
            Items =
            [
                new ComplexItem
                {
                    Text = "Stored one",
                    Tags = [new ComplexTag()],
                },
                new ComplexItem
                {
                    Text = "Stored two",
                    Tags = [new ComplexTag()],
                },
            ],
        };

        if (context is PlainPeerContext)
        {
            context.Attach(peer);
        }
        else
        {
            var root = new SharingRoot
            {
                Id = peer.Id,
                Peer = peer,
            };

            peer.Root = root;
            context.Attach(root);
            root.Name = "Pending root";
            context
                .Entry(root)
                .Property(value => value.Name)
                .IsModified = true;
        }

        _ = context.Model.GetRelationalModel();
        peer.Marker = "Pending marker";

        return context.Entry(peer);
    }

    /// <summary>Changes only CLR storage while automatic detection remains disabled.</summary>
    private static void ChangeCollection(
        ComplexPeer peer,
        string change
    )
    {
        switch (change)
        {
            case "edit":
                peer.Items[0].Text = "Raw edit";
                break;
            case "add":
                peer.Items.Add(new ComplexItem { Text = "Raw added" });
                break;
            case "remove":
                peer.Items.RemoveAt(0);
                break;
            case "replace":
                peer.Items = [new ComplexItem { Text = "Raw replacement" }];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    /// <summary>Obtains actual provider state without requesting global change detection.</summary>
    private static IUpdateEntry Update(
        DbContext context,
        object entity
    )
        => context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single(entry => ReferenceEquals(
                entry.ToEntityEntry()
                    .Entity,
                entity));

    /// <summary>Captures observable element references and independent current/original/flag/state storage.</summary>
    private static CollectionObservation ObserveCollection(
        EntityEntry<ComplexPeer> entry
    )
    {
        var collection = entry.ComplexCollection(value => value.Items);
        var values = entry.Entity.Items;
        var elements = new ElementObservation[values.Count];

        // WHY: CLR elements and actual EF element entries must be observed at the same collection ordinal.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < values.Count; index++)
        {
            var element = collection[index];
            var property = element.Property(nameof(ComplexItem.Text));
            elements[index] = new ElementObservation(
                values[index],
                element.State,
                property.CurrentValue,
                property.OriginalValue,
                property.IsModified,
                values[index].Tags);
        }

        return new CollectionObservation(values, collection.IsModified, elements);
    }

    /// <summary>Checks identities, current/original values, flags and element states after rollback.</summary>
    private static void AssertCollection(
        EntityEntry<ComplexPeer> entry,
        CollectionObservation before
    )
    {
        Assert.Same(before.Instance, entry.Entity.Items);
        Assert.Equal(before.Elements.Length, entry.Entity.Items.Count);
        var collection = entry.ComplexCollection(value => value.Items);

        Assert.Equal(before.Modified, collection.IsModified);

        // WHY: Exact ordinal assertions reject a replacement list or cloned elements with equal content.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Elements.Length; index++)
        {
            var expected = before.Elements[index];
            var element = collection[index];
            var property = element.Property(nameof(ComplexItem.Text));

            Assert.Same(expected.Instance, entry.Entity.Items[index]);
            Assert.Same(expected.Tags, entry.Entity.Items[index].Tags);
            Assert.Equal(expected.State, element.State);
            Assert.Equal(expected.Current, property.CurrentValue);
            Assert.Equal(expected.Original, property.OriginalValue);
            Assert.Equal(expected.Modified, property.IsModified);
        }
    }

    /// <summary>Retains test observations without introducing any alternate EF entry implementation.</summary>
    private sealed record CollectionObservation(
        List<ComplexItem> Instance,
        bool Modified,
        ElementObservation[] Elements
    );

    /// <summary>Pairs one actual complex element with its independently observed public EF state.</summary>
    private sealed record ElementObservation(
        ComplexItem Instance,
        EntityState State,
        object? Current,
        object? Original,
        bool Modified,
        List<ComplexTag> Tags
    );
}
