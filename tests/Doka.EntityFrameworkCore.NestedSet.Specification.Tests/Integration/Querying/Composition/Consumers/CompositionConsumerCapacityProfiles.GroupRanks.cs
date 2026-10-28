using Microsoft.Data.Sqlite;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionConsumerCapacityProfiles
{
    private static readonly int[] s_ordinalEligibleTypes = [1];

    /// <summary>Qualifies structural and eligible ordinals on the existing deep population without reseeding.</summary>
    /// <param name="database">The already populated structural database shared by every deep consumer profile.</param>
    /// <param name="engine">The owned provider whose actual command and native plan are retained.</param>
    /// <param name="data">The cached bounded payload, with one manager at the root and one source at the leaf.</param>
    private static async Task<IReadOnlyList<CompositionConsumerCapacityObservation>> MeasureGroupRanksAsync(
        TestDatabase database,
        string engine,
        CompositionConsumerDataset data
    )
    {
        (string Name, App.SupervisorGroups Rule, bool Found)[] profiles =
        [
            ("type-last-present", new App.NthTypeGroupThenValidate(1, data.Nodes - 1), true),
            ("type-after-last-absent", new App.NthTypeGroupThenValidate(1, data.Nodes), false),
            ("eligible-second-absent", new App.NthEligibleGroup(s_ordinalEligibleTypes, 2), false),
        ];

        var observations = new List<CompositionConsumerCapacityObservation>(profiles.Length);
        foreach (var profile in profiles)
        {
            observations.Add(await MeasureGroupRankAsync(
                database, engine, data, profile.Name, profile.Rule, profile.Found));
        }

        // WHY: A structural ordinal may enumerate the deep population once. Fixed linear pass allowances
        // admit window/grouping execution and rounded native observations, never a per-node hierarchy rescan.
        var exposedIteratorBudget = 64L * data.Nodes;
        var materializedOutputBudget = 4L * data.Nodes;
        foreach (var observation in observations)
        {
            if (engine == "MySql")
            {
                var work = CompositionPlanWork.ReadMySqlTree(observation.Plan);
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"{observation.Profile}; exposed iterator tuples={work.MaximumOperatorTuples}; "
                    + $"materialized output={work.MaximumMaterializedTuples}; "
                    + $"budgets={exposedIteratorBudget}/{materializedOutputBudget}.");
                // WHY: One source marker and one combined origin/outcome row remain, even for a missing ordinal.
                Assert.Equal(2D, work.RootOutputTuples);
                Assert.InRange(work.MaximumOperatorTuples, 0, exposedIteratorBudget);
                Assert.InRange(work.MaximumMaterializedTuples, 0, materializedOutputBudget);
            }
            else
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"{observation.Profile}; native plan retained; no numerical native-work or physical-I/O claim.");
            }
        }

        return observations;
    }

    /// <summary>Measures one-reader ordinals with complete scalar outcome, provenance and cursor evidence.</summary>
    private static async Task<CompositionConsumerCapacityObservation> MeasureGroupRankAsync(
        TestDatabase database,
        string engine,
        CompositionConsumerDataset data,
        string name,
        App.SupervisorGroups rule,
        bool found
    )
    {
        await using var source = database.CreateContext();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var commands = new CompositionCommandProbe();
        var connectionString = source.Database.GetConnectionString()!;
        if (engine == "Sqlite")
        {
            // WHY: A fresh DbContext can reuse a pooled native handle. This read owns its progress handler
            // exclusively and must never overwrite a callback installed by another connection owner.
            connectionString = new SqliteConnectionStringBuilder(connectionString) { Pooling = false }.ToString();
        }

        var options = new DbContextOptionsBuilder<CompositionCapacityGroupContext>(
                ModelCompatibilityDatabase.Options<CompositionCapacityGroupContext>(engine,
                    connectionString))
            .AddInterceptors(planProbe, commands, TestMaterializationObserver.Instance).Options;

        await using var context = new CompositionCapacityGroupContext(options);
        var materialized = 0;
        TestMaterializationObserver.Instance.Register(context, _ => materialized++);
        var request = CompositionConsumerDataset.Supervisors(1, 1) with { Groups = rule };
        var tenant = CompositionConsumerModel.Tenant;
        var buildBytes = Construction(() => App.SupervisorQueries.Candidates(context, tenant, request));
        var constructionCommands = commands.CommandCount;
        var profile = $"groups-chain-{data.Nodes}-ordinal-{name}-initial-build";
        SqliteInstructionBudget? instructionBudget = null;
        var instructionLimit = 1_024L * data.Nodes;
        if (engine == "Sqlite")
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            instructionBudget = new SqliteInstructionBudget(
                (SqliteConnection)context.Database.GetDbConnection(), instructionLimit);
        }

        var started = Stopwatch.GetTimestamp();
        App.SupervisorResolutionBatch batch;
        try
        {
            batch = await App.SupervisorQueries.ResolveAsync(context, tenant, request,
                App.SupervisorSelection.RequireUnique, CancellationToken.None);
        }
        catch (Exception error)
        {
            instructionBudget?.Dispose();
            await RetainReadFailureAsync(context, engine, profile, planProbe, error);

            throw;
        }
        finally
        {
            // WHY: EXPLAIN and failure diagnostics have their own work; only the actual application read
            // belongs to this allowance. Removal also keeps the native handle safe after an interrupt.
            instructionBudget?.Dispose();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        if (instructionBudget is not null)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{profile}; approximate SQLite instruction quanta={instructionBudget.ObservedInstructions}; "
                + $"quantum={SqliteInstructionBudget.Quantum}; budget={instructionLimit}.");
            Assert.False(instructionBudget.Exceeded);
            Assert.InRange(instructionBudget.ObservedInstructions, SqliteInstructionBudget.Quantum, instructionLimit);
        }

        var commandCount = commands.CommandCount;
        var maximumParameters = planProbe.ParameterBindings.Max(bindings => bindings.Length);
        var tracked = context.ChangeTracker.Entries().Count();
        var actual = GroupRankSignature(batch);
        var expected = ExpectedGroupRank(data.Nodes, found);
        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, 0, engine);
        var observation = new CompositionConsumerCapacityObservation(profile, actual, expected, commandCount,
            1, maximumParameters, constructionCommands, materialized, tracked, buildBytes, 256 * 1_024, elapsed, plan);

        await WriteAsync(engine, observation, planProbe.Commands[0]);

        return observation;
    }

    /// <summary>Retains every status and scalar cursor field instead of reducing success to a winner ID.</summary>
    private static string[] GroupRankSignature(App.SupervisorResolutionBatch batch)
    {
        var rows = new List<string> { $"{batch.Status}:{batch.Complete}" };
        foreach (var origin in batch.Origins.OrderBy(origin => origin.StartGroupId))
        {
            var cursor = origin.Continuation is { } continuation ? GroupRankEvidenceKey(continuation) : "none";
            rows.Add($"origin:{origin.StartGroupId}:{origin.Status}:{origin.Evidence.Count}:cursor:{cursor}");
            rows.AddRange(origin.Evidence.OrderBy(row => row.FoundGroupId).ThenBy(row => row.CandidateUserId)
                .Select(row => "evidence:" + GroupRankEvidenceKey(row)));
        }

        return rows.ToArray();
    }

    /// <summary>Derives the final structural rank and sole eligible root from known direct-parent adjacency.</summary>
    private static string[] ExpectedGroupRank(int nodes, bool found)
    {
        if (!found)
        {
            return ["Completed:True", $"origin:{nodes}:NotFound:0:cursor:none"];
        }

        var evidence = new App.SupervisorCandidate
        {
            TenantId = CompositionConsumerModel.Tenant,
            SourceUserId = CompositionConsumerDataset.SourceUser(1),
            StartGroupId = nodes, StartTreeId = CompositionConsumerModel.Tree,
            FoundGroupId = 1, CandidateUserId = 2, Distance = nodes - 1, LeadershipPriority = 1,
        };

        var key = GroupRankEvidenceKey(evidence);

        return ["Completed:True", $"origin:{nodes}:Found:1:cursor:{key}", "evidence:" + key];
    }

    /// <summary>Canonicalizes complete scalar provenance without reading or buffering any hierarchy nodes.</summary>
    private static string GroupRankEvidenceKey(App.SupervisorCandidate row)
        => $"{row.TenantId:N}:{row.SourceUserId}:{row.StartGroupId}:{row.StartTreeId:N}:"
            + $"{row.FoundGroupId}:{row.CandidateUserId}:{row.Distance}:{row.LeadershipPriority}";
}
