namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingQueryShapeTests
{
    /// <summary>Excludes stale same-scope and foreign aliases according to their persisted scope and bounds.</summary>
    [Fact]
    public async Task PersistedIntervalsExcludeStaleTrackedAliases()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        using var probe = new ScaleProbe();
        var ordinals = new OrdinalProbe();
        await using var context = CreateContext(setup, probe, ordinals);
        var renamed = CreateNode(1, 0, true);
        var sibling = CreateNode(1, 1, true);
        var distant = CreateNode(1, 189, true);
        var foreign = CreateNode(2, 0, true);

        // WHY: Attaching stale snapshots makes their values original, rather than requesting a structural edit.
        // The distant alias falsely overlaps the changed interval; the foreign alias belongs to another scope.
        distant.Left = 2;
        distant.Right = 3;
        distant.Position = 0;
        foreign.Left = 9999;
        foreign.Right = 10000;
        foreign.Position = 4999;
        context.AttachRange(renamed, sibling, distant, foreign);
        var distantBefore = Coordinates(distant);
        var foreignBefore = Coordinates(foreign);
        renamed.Name = "N000-bz";
        probe.Observe(context);

        // Act
        var saved = await context.SaveChangesAsync(false, CancellationToken.None);

        // Assert
        var observation = Capture(probe, ordinals);
        Assert.Equal(1, saved);
        Assert.Equal(0, probe.ActiveReaders);
        Assert.Equal(2, observation.Commands.Length);
        Assert.Equal(2, observation.Readers.Length);
        Assert.Equal(
            observation.Commands.Select(command => command.Sql),
            observation.Readers.Select(reader => reader.Sql));

        Assert.Equal<int>([0], observation.Readers[0].Ordinals);
        Assert.Equal(
            [0, 1],
            observation
                .Readers[1]
                .Ordinals
                .OrderBy(value => value));
        Assert.Equal((4, 5, 1), (renamed.Left, renamed.Right, renamed.Position));
        Assert.Equal((2, 3, 0), (sibling.Left, sibling.Right, sibling.Position));
        Assert.Equal(distantBefore, Coordinates(distant));
        Assert.Equal(foreignBefore, Coordinates(foreign));
        Assert.Equal(
            2,
            context
                .Entry(distant)
                .Property(node => node.Left)
                .OriginalValue);
        Assert.Equal(
            9999,
            context
                .Entry(foreign)
                .Property(node => node.Left)
                .OriginalValue);
        Assert.True(
            context
                .Entry(renamed)
                .Property(node => node.Name)
                .IsModified);
        AssertPersisted(await ReadScopeAsync(setup, 1), 1);
        AssertPersisted(await ReadScopeAsync(setup, 2), 0);
        await AssertRootAsync(setup, 1);
        await AssertRootAsync(setup, 2);
        Assert.All(
            observation.Commands,
            command =>
            {
                Assert.DoesNotContain(nameof(OrderingKeyNode<,>.Name), command.Sql, StringComparison.Ordinal);
                Assert.InRange(command.Sql.Length, 1, MaximumRefreshSqlCharacters);
            });
    }
}
