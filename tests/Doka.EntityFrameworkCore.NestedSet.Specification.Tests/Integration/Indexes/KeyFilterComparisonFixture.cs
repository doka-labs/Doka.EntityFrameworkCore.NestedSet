namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Separates an immutable comparison seed from sibling cases that reset their own rows.</summary>
public sealed class KeyFilterComparisonFixture : IAsyncLifetime
{
    private readonly RelationalFixture _resettable = new();
    private readonly SortedDictionary<(int Count, int Representation), string[]> _evidence = [];
    private Task<TestDatabase>? _comparison;
    private string? _engine;

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Resets the original mutable database used by native-type and collection-mode cases.</summary>
    /// <param name="engine">The immutable engine supplied by the consuming provider fixture.</param>
    /// <returns>The existing class-owned database with empty rows.</returns>
    internal Task<TestDatabase> ResetAsync(
        string engine
    ) => _resettable.ResetAsync(engine);

    /// <summary>Returns the same read-only seed without allowing sibling resets to invalidate it.</summary>
    /// <param name="engine">The immutable engine supplied by the consuming provider fixture.</param>
    /// <returns>The fixture-owned database; each case owns only its newly created context.</returns>
    internal Task<TestDatabase> ComparisonAsync(
        string engine
    )
    {
        if (_engine is not null
            && _engine != engine)
        {
            throw new InvalidOperationException("A comparison fixture cannot change its provider engine.");
        }

        _engine = engine;
        // WHY: Collection-mode and native-type cases reset their original database in arbitrary discovery order.
        // One separate immutable database avoids sixteen seeds without coupling cases to execution order.
        return _comparison ??= CreateComparisonAsync(engine);
    }

    /// <summary>Retains copied evidence in the original cardinality and representation order.</summary>
    /// <param name="representation">The discovered integer, Guid, binary, or string case.</param>
    /// <param name="count">The requested membership cardinality.</param>
    /// <param name="evidence">The three query forms and their native plan observations.</param>
    internal void RecordEvidence(
        string representation,
        int count,
        IReadOnlyList<string> evidence
    )
    {
        var order = representation switch
        {
            "Int32" => 0,
            "Guid" => 1,
            "Binary" => 2,
            "String" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(representation)),
        };

        _evidence.Add((count, order), evidence.ToArray());
    }

    /// <summary>Writes one complete legacy evidence file and releases both class-owned databases.</summary>
    public ValueTask DisposeAsync() => QueryPlanTestSupport.CompleteFixtureAsync(WriteEvidenceAsync, CleanupAsync);

    /// <summary>Writes the complete comparison artifact only when this suite observed comparison cases.</summary>
    private Task WriteEvidenceAsync() => _evidence.Count == 0
        ? Task.CompletedTask
        : QueryPlanTestSupport.WriteEvidenceAsync(
            _engine + "-key-filter",
            _evidence
                .Values
                .SelectMany(entries => entries)
                .ToArray());

    /// <summary>Returns both owned database cleanups without wrapping away a secondary task failure.</summary>
    private Task CleanupAsync()
    {
        // WHY: Reading a successfully completed initialization cannot block. Returning WhenAll itself lets the
        // shared teardown boundary retain both database failures rather than only the exception exposed by await.
        var comparisonCleanup = _comparison is { IsCompletedSuccessfully: true }
            ? _comparison
                .Result
                .DisposeAsync()
                .AsTask()
            : Task.CompletedTask;

        return Task.WhenAll(
            _resettable
                .DisposeAsync()
                .AsTask(),
            comparisonCleanup);
    }

    /// <summary>Seeds all four mapped key representations exactly once using the existing server owner.</summary>
    private static async Task<TestDatabase> CreateComparisonAsync(
        string engine
    )
    {
        var database = await TestDatabase.CreateAsync(engine);

        try
        {
            await using var context = database.CreateContext();

            for (var value = 1; value <= 256; value++)
            {
                var identity = new Guid(

                    value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                await context.AddAsync(
                    new TreeNode
                    {
                        NodeId = value,
                        Tree = 1,
                        TreeId = identity,
                        Start = 1,
                        End = 2
                    },
                    CancellationToken.None);

                await context.AddAsync(
                    new GuidNode
                    {
                        Id = identity,
                        Tree = 1,
                        TreeId = identity,
                        Left = 1,
                        Right = 2
                    },
                    CancellationToken.None);

                await context.AddAsync(
                    new BinaryNode
                    {
                        Id = BitConverter.GetBytes(value),
                        Tree = 1,
                        TreeId = identity,
                        Left = 1,
                        Right = 2,
                    },
                    CancellationToken.None);

                await context.AddAsync(
                    new TextNode
                    {
                        Id = $"Node-{value:D4}",
                        Tree = "scope",
                        TreeId = identity,
                        Left = 1,
                        Right = 2
                    },
                    CancellationToken.None);
            }

            await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
            await QueryPlanTestSupport.RefreshStatisticsAsync<TreeNode>(context, engine);
            await QueryPlanTestSupport.RefreshStatisticsAsync<GuidNode>(context, engine);
            await QueryPlanTestSupport.RefreshStatisticsAsync<BinaryNode>(context, engine);
            await QueryPlanTestSupport.RefreshStatisticsAsync<TextNode>(context, engine);

            return database;
        }
        catch (Exception initializationFailure)
        {
            try
            {
                await database.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Comparison seed initialization and cleanup both failed.",
                    initializationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }
}
