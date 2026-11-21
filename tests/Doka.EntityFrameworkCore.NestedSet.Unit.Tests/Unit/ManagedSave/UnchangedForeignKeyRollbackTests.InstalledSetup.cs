using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>Builds the exact pending graph before any snapshot observes its CLR or native lanes.</summary>
    private static PendingForeignKeyGraph PrepareForeignKeyGraph(
        RollbackContext context,
        bool composite,
        bool originallyNull
    )
    {
        var original = new RollbackOwner { Id = 2 };
        var transient = new RollbackOwner
        {
            Id = 1,
            Scope = 9,
        };

        var reviewer = new RollbackOwner { Id = 4 };
        var first = new RollbackNode
        {
            Id = 10,
            OwnerId = original.Id,
            Owner = original,
        };

        var last = new RollbackNode
        {
            Id = 30,
            OwnerId = original.Id,
            Owner = original,
        };

        var node = new RollbackNode
        {
            Id = 20,
            OwnerId = originallyNull
                ? null
                : original.Id,
            Owner = originallyNull
                ? null
                : original,
            ReviewerId = composite
                ? reviewer.Id
                : null,
            Reviewer = composite
                ? reviewer
                : null,
        };

        original.Nodes.Add(first);

        if (!originallyNull)
        {
            original.Nodes.Add(node);
        }

        original.Nodes.Add(last);
        context.AttachRange(original, transient, reviewer);
        var entry = context.Attach(node);

        if (!originallyNull)
        {
            // WHY: A raw ordered list may contain duplicate references while EF stores one native member.
            // Rollback must restore the caller's exact pending order, independently of that native set.
            original.Nodes.Remove(node);
            original.Nodes.Insert(0, node);
            original.Nodes.Add(node);
        }

        node.Name = "Pending name";
        entry.Property(value => value.Name)
            .IsModified = true;

        return new PendingForeignKeyGraph(original, transient, reviewer, node, entry);
    }

    /// <summary>Captures the independent scalar lanes before the callback changes installed relationships.</summary>
    private static ForeignKeyFrame CaptureForeignKeyPreimage(
        RollbackContext context,
        in PendingForeignKeyGraph graph,
        bool allEntries
    )
    {
        var update = Update(context, graph.Node);
        var originalUpdate = Update(context, graph.Original);
        var transientUpdate = Update(context, graph.Transient);
        var updates = context.GetService<IUpdateAdapterFactory>().Create();
        var reference = graph.Entry.Metadata.FindNavigation(nameof(RollbackNode.Owner))!;
        var key = reference.ForeignKey;
        var inverse = key.PrincipalToDependent!;
        var values = new ForeignKeyValues(
            Installed(update, key),
            key.Properties.Select(update.GetCurrentValue).ToArray(),
            key.Properties.Select(update.IsModified).ToArray(),
            key.Properties.Select(update.GetOriginalValue).ToArray());

        var membership = CaptureForeignKeyMembership(
            graph, update, originalUpdate, transientUpdate, reference, inverse);
        var snapshot = allEntries
            ? NestedSetTrackerSnapshot.Capture(context)
            : NestedSetTrackerSnapshot.Capture(context, [update]);

        return new ForeignKeyFrame(update, originalUpdate, transientUpdate, updates, reference, inverse,
            values, membership, snapshot);
    }

    /// <summary>Retains original collection instances, raw ordering and native sets as separate observations.</summary>
    private static ForeignKeyMembership CaptureForeignKeyMembership(
        in PendingForeignKeyGraph graph,
        IUpdateEntry update,
        IUpdateEntry originalUpdate,
        IUpdateEntry transientUpdate,
        INavigation reference,
        INavigation inverse
    )
    {
        return new ForeignKeyMembership(
            graph.Node.Owner,
            update.GetRelationshipSnapshotValue(reference),
            graph.Original.Nodes,
            graph.Original.Nodes.ToArray(),
            graph.Transient.Nodes,
            graph.Transient.Nodes.ToArray(),
            Members(originalUpdate, inverse),
            Members(transientUpdate, inverse),
            graph.Reviewer.ReviewNodes.ToArray());
    }

    /// <summary>Installs the callback relationship and records its hidden native bucket before rollback.</summary>
    private static HiddenForeignKey ApplyForeignKeyCallback(
        in PendingForeignKeyGraph graph,
        in ForeignKeyFrame frame,
        bool originallyNull,
        bool marked,
        bool composite
    )
    {
        graph.Entry.Reference(value => value.Owner)
            .CurrentValue = graph.Transient;

        graph.Node.OwnerId = originallyNull
            ? null
            : graph.Original.Id;

        graph.Node.OwnerScope = graph.Original.Scope;
        graph.Entry.Property(value => value.OwnerId)
            .IsModified = marked;

        if (composite)
        {
            graph.Entry.Property(value => value.OwnerScope)
                .IsModified = marked;
        }

        var hiddenInstalled = Installed(frame.Update, frame.Reference.ForeignKey);
        var hiddenBucket = frame.Updates
            .GetDependents(frame.TransientUpdate, frame.Reference.ForeignKey)
            .Contains(frame.Update);

        if (marked && !originallyNull)
        {
            if (!graph.Original.Nodes.Contains(graph.Node))
            {
                graph.Original.Nodes.Add(graph.Node);
            }

            frame.Snapshot.RecordPersistedAddition(frame.OriginalUpdate, frame.Inverse, graph.Node);
            NestedSetInsertionContract.Instance.AddCollectionSnapshot(frame.OriginalUpdate, frame.Inverse, graph.Node);
        }

        return new HiddenForeignKey(hiddenInstalled, hiddenBucket);
    }
}
