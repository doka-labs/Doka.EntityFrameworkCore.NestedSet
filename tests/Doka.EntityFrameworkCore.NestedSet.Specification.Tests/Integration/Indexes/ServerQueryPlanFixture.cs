namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Owns one immutable wide/deep seed and the complete evidence from its independently discovered queries.
/// </summary>
public sealed class ServerQueryPlanFixture : IAsyncLifetime
{
    private readonly OrderingFixture _fixture = new();
    private readonly SortedDictionary<(int Scope, int Offset, int Query), string> _evidence = [];
    private Task? _initialization;
    private string? _engine;

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Returns a fresh context after arranging this class's four-thousand-node seed once.</summary>
    /// <param name="engine">The immutable engine supplied by the concrete provider fixture.</param>
    /// <param name="probe">The command observer owned by the individual test case.</param>
    /// <returns>A context owned and disposed by the calling case.</returns>
    internal async Task<OrderingContext> CreateContextAsync(
        string engine,
        EnterpriseProbe probe
    )
    {
        if (_engine is not null
            && _engine != engine)
        {
            throw new InvalidOperationException("A query-plan fixture cannot change its provider engine.");
        }

        _engine = engine;

        // WHY: All cases are read-only and xUnit serializes this concrete class's collection. Retaining the
        // initialization task prevents per-row resets without introducing another container or shared global seed.
        await (_initialization ??= InitializeSeedAsync(engine));

        return await _fixture.CreateContextAsync(engine, "Strict", probe);
    }

    /// <summary>Retains one query observation in the original scope, anchor and query-kind order.</summary>
    internal void RecordEvidence(
        int scope,
        int offset,
        string query,
        string plan
    )
    {
        var order = query switch
        {
            "Parent" => 0,
            "Ancestors" => 1,
            "Descendants" => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };

        _evidence.Add((scope, offset, order), plan);
    }

    /// <summary>Retains the configured-predecessor observation after all hierarchy query scenarios.</summary>
    internal void RecordPredecessorEvidence(
        string plan
    ) => _evidence.Add((int.MaxValue, 0, 0), "configured-predecessor\n" + plan);

    /// <summary>Writes one complete legacy plan artifact and releases the original ordering resource.</summary>
    public ValueTask DisposeAsync() => QueryPlanTestSupport.CompleteFixtureAsync(
        WriteEvidenceAsync,
        () => _fixture
            .DisposeAsync()
            .AsTask());

    /// <summary>Writes the complete hierarchy artifact only when this suite observed a native plan.</summary>
    private Task WriteEvidenceAsync() => _evidence.Count == 0
        ? Task.CompletedTask
        : QueryPlanTestSupport.WriteEvidenceAsync(_engine + "-hierarchy", _evidence.Values.ToArray());

    /// <summary>Creates and analyzes the original dataset before any observed query executes.</summary>
    private async Task InitializeSeedAsync(
        string engine
    )
    {
        await using var setup = await _fixture.ResetAsync(engine);
        await SeedAsync(setup);
        await QueryPlanTestSupport.RefreshStatisticsAsync<OrderingNode>(setup, engine);
    }

    /// <summary>
    /// Seeds twenty overlapping scopes, alternating wide siblings and deep chains, without timed inserts.
    /// </summary>
    private static async Task SeedAsync(
        OrderingContext context
    )
    {
        for (var scope = 1; scope <= 20; scope++)
        {
            for (var offset = 0; offset < 200; offset++)
            {
                var root = scope * 1000;
                var wide = scope % 2 == 0;
                await context.AddAsync(
                    new OrderingNode
                    {
                        Id = root + offset,
                        Scope = scope,
                        Name = $"Node-{offset:D4}",
                        ParentId = offset == 0
                            ? null
                            : wide
                                ? root
                                : root + offset - 1,
                        Left = wide ? offset == 0 ? 1 : offset * 2 : offset + 1,
                        Right = wide ? offset == 0 ? 400 : offset * 2 + 1 : 400 - offset,
                        Depth = wide ? offset == 0 ? 0 : 1 : offset,
                        Position = wide && offset != 0 ? offset - 1 : 0,
                    },
                    CancellationToken.None);
            }
        }

        using (NestedSetSaveChanges.EnterManagedSave(
                   context,
                   context
                       .ChangeTracker
                       .Entries()
                       .Select(entry => entry.Entity),
                   static () => { }))
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }

        context.ChangeTracker.Clear();
    }
}
