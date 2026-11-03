using System.Collections;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>An installed intended-parent snapshot cannot hide its later inverse-collection severance.</summary>
    /// <param name="removeInverse">Whether a callback removes the child from the intended parent's collection.</param>
    /// <param name="sourceRoot">Whether the suppressed physical FK belongs to an independent root.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PlannedParentCollectionRemovalUsesIntentDespiteSuppressedPhysicalForeignKey(
        bool removeInverse,
        bool sourceRoot
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var original = CreateNode(1);
        var target = CreateNode(3);
        var child = CreateNode(2);

        if (sourceRoot)
        {
            // WHY: A root's intended cross-tree destination must differ from its still-suppressed source identity.
            target.TreeId = Guid.Parse("9b947be4-32ac-4708-9032-b440242df2bb");
            original.TreeId = Guid.Parse("4394ff64-80a8-4ddd-a158-1af18037c28c");
        }
        else
        {
            child.ParentId = original.Id;
            child.Parent = original;
            original.Children!.Add(child);
        }

        context.AttachRange(original, target, child);
        var entry = context.Entry(child);
        var parent = entry.Property(node => node.ParentId);
        var update = UpdateEntry(context, child);
        var targetUpdate = UpdateEntry(context, target);
        var parentNavigation = entry.Metadata.FindNavigation(nameof(ProbeNode.Parent))!;
        var inverse = parentNavigation.Inverse!;
        // WHY: Public EF assignment performs genuine fixup and installs the new parent's relationship snapshot.
        parent.CurrentValue = target.Id;
        var installedPrincipal = update.GetRelationshipSnapshotValue(parentNavigation);
        var plan = CapturePlan([entry]);
        // WHY: The raw reset preserves that installed snapshot while matching the save's suppressed physical FK.
        child.ParentId = sourceRoot ? null : original.Id;
        parent.IsModified = false;
        CaptureSuppressedCollectionPlan(plan, update);

        if (removeInverse)
        {
            target.Children!.Remove(child);
        }

        var parentBefore = child.Parent;
        var physicalBefore = child.ParentId;
        var installedMembers = (targetUpdate.GetRelationshipSnapshotValue(inverse) as IEnumerable<object> ?? [])
            .ToArray();

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.Same(target, installedPrincipal);
        Assert.Same(target, parentBefore);
        Assert.Equal(sourceRoot ? null : (int?)original.Id, physicalBefore);
        Assert.Contains(child, installedMembers);
        Assert.Equal(removeInverse, changed);
        Assert.Equal(!removeInverse, target.Children!.Contains(child));
        Assert.Same(parentBefore, child.Parent);
        Assert.Equal(physicalBefore, child.ParentId);
        Assert.False(parent.IsModified);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Uses the existing private plan boundary without adding a production test seam.</summary>
    private static void CaptureSuppressedCollectionPlan(
        object plan,
        IUpdateEntry update
    )
    {
        var candidate = ((IDictionary)plan)[update]!;
        var capture = candidate
            .GetType()
            .GetMethod("CaptureSuppressedParent",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

        capture.Invoke(candidate, BindingFlags.DoNotWrapExceptions, null, [update], null);
    }
}
