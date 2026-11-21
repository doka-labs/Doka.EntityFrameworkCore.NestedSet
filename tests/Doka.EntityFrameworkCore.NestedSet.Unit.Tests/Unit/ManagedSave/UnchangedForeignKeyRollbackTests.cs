using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves initially unchanged FK identities independently of raw callback scalar resets.</summary>
public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>An initially unchanged FK restores its exact native maps, references and pending CLR graph.</summary>
    /// <param name="composite">Whether ordinary FKs share a composite scope member.</param>
    /// <param name="originallyNull">Whether the nullable ordinary relationship initially has no owner.</param>
    /// <param name="marked">Whether the callback marks the raw reset as an ordinary persisted FK write.</param>
    /// <param name="allEntries">Whether the snapshot captures the whole tracker or its selected write entry.</param>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public async Task Restore_UnchangedForeignKey_RestoresInstalledMapsAndPendingGraph(
        bool composite,
        bool originallyNull,
        bool marked,
        bool allEntries
    )
    {
        // Arrange
        await using var context = composite
            ? new CompositeRollbackContext()
            : new RollbackContext();

        var graph = PrepareForeignKeyGraph(context, composite, originallyNull);
        var frame = CaptureForeignKeyPreimage(context, graph, allEntries);
        var hidden = ApplyForeignKeyCallback(graph, frame, originallyNull, marked, composite);

        // Act
        frame.Snapshot.Restore();

        // WHY: A second cleanup must not replay a retired inverse delta over the restored original member.
        frame.Snapshot.Restore();

        // Assert
        AssertInstalledForeignKey(graph, frame, hidden);
        AssertForeignKeyMembership(graph, frame, originallyNull);
        AssertForeignKeyPayload(graph);
        AssertForeignKeyReviewer(context, graph, frame, composite);
        AssertForeignKeyTracker(context);
    }

    /// <summary>Resolves the exact native entry without asking EF to detect any pending raw changes.</summary>
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

    /// <summary>Copies the installed native FK vector independently of current and original scalar storage.</summary>
    private static object?[] Installed(
        IUpdateEntry entry,
        IForeignKey key
    )
        => key
            .Properties
            .Select(entry.GetRelationshipSnapshotValue)
            .ToArray();

    /// <summary>Copies only actual native inverse membership, preserving absent storage as an empty set.</summary>
    private static object[] Members(
        IUpdateEntry entry,
        INavigation navigation
    )
        => (entry.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? []).ToArray();
}
