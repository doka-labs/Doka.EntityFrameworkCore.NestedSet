using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Tracks a valid initial graph with scalar originals before creating raw pending values.</summary>
    private static EntityEntry<TopologyPayload> TrackGraph(
        TopologyContext context
    )
    {
        var payload = new TopologyPayload
        {
            Id = 7,
            Rows =
            [
                new TopologyItem
                {
                    Text = "Stored first",
                    Leaves = [new TopologyLeaf { Text = "Stored first leaf" }],
                },
                new TopologyItem
                {
                    Text = "Stored last",
                    Leaves = [new TopologyLeaf { Text = "Stored last leaf" }],
                },
            ],
        };

        if (context is SharedTopologyContext)
        {
            var owner = new TopologyOwner
            {
                Id = payload.Id,
                Payload = payload,
            };

            payload.Owner = owner;
            context.Attach(owner);
            owner.Name = "Pending owner";
            context
                .Entry(owner)
                .Property(value => value.Name)
                .IsModified = true;
        }
        else
        {
            context.Attach(payload);
        }

        _ = context.Model.GetRelationalModel();
        var rows = payload.Rows!;
        payload.Marker = "Pending marker";
        rows[0].Text = "Pending first";
        rows[0].Details!.Code = "Pending code";
        rows[0].Leaves![0].Text = "Pending leaf";

        return context.Entry(payload);
    }

    /// <summary>Tracks an unrelated preexisting entry whose raw pending value must not be lost on recovery.</summary>
    private static EntityEntry<UnrelatedPayload> TrackUnrelated(
        TopologyContext context
    )
    {
        var payload = new UnrelatedPayload { Id = 19 };
        var entry = context.Attach(payload);
        payload.Name = "Pending unrelated";

        return entry;
    }

    /// <summary>Changes caller-owned topology only after the library has already captured its preimage.</summary>
    private static void Mutate(
        TopologyPayload payload,
        string change
    )
    {
        switch (change)
        {
            case "removeFirst":
                payload.Rows!.RemoveAt(0);
                break;
            case "removeLast":
                payload.Rows!.RemoveAt(payload.Rows!.Count - 1);
                break;
            case "clear":
                payload.Rows!.Clear();
                break;
            case "nestedClear":
                payload.Rows![0].Leaves!.Clear();
                break;
            case "nestedNull":
                payload.Rows![0].Leaves = null;
                break;
            case "collectionNull":
                payload.Rows = null;
                break;
            case "optionalNull":
                payload.Optional = null;
                break;
            case "optionalReplace":
                payload.Optional = new TopologyDetails
                {
                    Text = "Callback details",
                    Code = "Callback code",
                };

                break;
            case "elementOptionalNull":
                payload.Rows![0].Details = null;
                break;
            case "listReplace":
                payload.Rows =
                [
                    new TopologyItem
                    {
                        Text = "Callback item",
                        Leaves = [],
                    },
                ];

                break;
            case "reverse":
                payload.Rows!.Reverse();
                break;
            case "add":
                payload.Rows!.Add(
                    new TopologyItem
                    {
                        Text = "Callback added",
                        Leaves = [],
                    });

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    /// <summary>Obtains the actual context-owned update entry without global detection.</summary>
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

    /// <summary>Checks that model-only recovery leaves detection settings and database I/O unchanged.</summary>
    private static void AssertSettings(
        TopologyContext context
    )
    {
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(CascadeTiming.Immediate, context.ChangeTracker.CascadeDeleteTiming);
        Assert.Equal(CascadeTiming.Immediate, context.ChangeTracker.DeleteOrphansTiming);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
