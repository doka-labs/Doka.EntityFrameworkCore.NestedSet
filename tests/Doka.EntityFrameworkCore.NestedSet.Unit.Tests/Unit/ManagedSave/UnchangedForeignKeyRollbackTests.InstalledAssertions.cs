namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>Checks native scalar identity and installed reference restoration.</summary>
    private static void AssertInstalledForeignKey(
        in PendingForeignKeyGraph graph,
        in ForeignKeyFrame frame,
        in HiddenForeignKey hidden
    )
    {
        Assert.Equal(frame.Values.Installed, frame.Values.Current);
        Assert.Equal(frame.Values.Original, frame.Values.Current);
        Assert.All(frame.Values.Modified, modified => Assert.False(modified));
        Assert.True(hidden.Bucket);
        Assert.Equal(
            frame.Reference.ForeignKey.PrincipalKey.Properties.Select(frame.TransientUpdate.GetCurrentValue),
            hidden.Installed);
        Assert.Equal(frame.Values.Installed, Installed(frame.Update, frame.Reference.ForeignKey));
        Assert.Equal(
            frame.Values.Original,
            frame.Reference.ForeignKey.Properties.Select(frame.Update.GetOriginalValue));
        Assert.Equal(
            frame.Values.Installed,
            frame.Reference.ForeignKey.Properties.Select(frame.Update.GetCurrentValue));
        Assert.Same(frame.Membership.Reference, graph.Node.Owner);
        Assert.Same(frame.Membership.InstalledReference, frame.Update.GetRelationshipSnapshotValue(frame.Reference));
    }

    /// <summary>Checks exact raw ordering and independent native inverse memberships.</summary>
    private static void AssertForeignKeyMembership(
        in PendingForeignKeyGraph graph,
        in ForeignKeyFrame frame,
        bool originallyNull
    )
    {
        Assert.Same(frame.Membership.OriginalCollection, graph.Original.Nodes);
        Assert.Equal(frame.Membership.OriginalMembers, graph.Original.Nodes, ReferenceEqualityComparer.Instance);
        Assert.Same(frame.Membership.TransientCollection, graph.Transient.Nodes);
        Assert.Equal(frame.Membership.TransientMembers, graph.Transient.Nodes, ReferenceEqualityComparer.Instance);
        Assert.True(
            frame.Membership.NativeOriginal
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(Members(frame.OriginalUpdate, frame.Inverse)));

        Assert.True(
            frame.Membership.NativeTransient
                .ToHashSet(ReferenceEqualityComparer.Instance)
                .SetEquals(Members(frame.TransientUpdate, frame.Inverse)));

        Assert.Equal(
            !originallyNull,
            frame.Updates
                .GetDependents(frame.OriginalUpdate, frame.Reference.ForeignKey)
                .Contains(frame.Update));

        Assert.DoesNotContain(
            frame.Update,
            frame.Updates.GetDependents(frame.TransientUpdate, frame.Reference.ForeignKey));

        var update = frame.Update;
        Assert.All(frame.Reference.ForeignKey.Properties, property => Assert.False(update.IsModified(property)));
    }

    /// <summary>Checks that unrelated pending payload retains its value, original and modification intent.</summary>
    private static void AssertForeignKeyPayload(
        in PendingForeignKeyGraph graph
    )
    {
        Assert.Equal("Pending name", graph.Node.Name);
        Assert.Equal(
            "Before",
            graph.Entry.Property(value => value.Name)
                .OriginalValue);

        Assert.True(
            graph.Entry.Property(value => value.Name)
                .IsModified);

        Assert.Equal(EntityState.Modified, graph.Entry.State);
    }

    /// <summary>Checks that restoring the first FK preserves a second relationship sharing its scope.</summary>
    private static void AssertForeignKeyReviewer(
        RollbackContext context,
        in PendingForeignKeyGraph graph,
        in ForeignKeyFrame frame,
        bool composite
    )
    {
        if (composite)
        {
            var reviewerReference = graph.Entry.Metadata.FindNavigation(nameof(RollbackNode.Reviewer))!;
            var reviewerUpdate = Update(context, graph.Reviewer);

            Assert.Same(graph.Reviewer, graph.Node.Reviewer);
            Assert.Same(graph.Reviewer, frame.Update.GetRelationshipSnapshotValue(reviewerReference));
            Assert.Contains(frame.Update, frame.Updates.GetDependents(reviewerUpdate, reviewerReference.ForeignKey));
            Assert.Equal(
                frame.Membership.ReviewerMembers,
                graph.Reviewer.ReviewNodes,
                ReferenceEqualityComparer.Instance);
        }
    }

    /// <summary>Checks that observation and rollback did not turn on detection or open the connection.</summary>
    private static void AssertForeignKeyTracker(
        RollbackContext context
    )
    {
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
