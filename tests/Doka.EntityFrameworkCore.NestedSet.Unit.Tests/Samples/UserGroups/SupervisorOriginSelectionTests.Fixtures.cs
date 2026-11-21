namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SupervisorOriginSelectionTests
{
    /// <summary>Constructs a completed batch with every explicitly evaluated origin.</summary>
    /// <param name="origins">The origins retained in their explicit application order.</param>
    private static SupervisorResolutionBatch Batch(
        params SupervisorResolution[] origins
    )
        => new(SupervisorBatchStatus.Completed, origins);

    /// <summary>Constructs an All-style found origin without asserting unique person selection in advance.</summary>
    /// <param name="group">The own-membership origin group.</param>
    /// <param name="evidence">Every candidate occurrence retained for this origin.</param>
    private static SupervisorResolution Found(
        int group,
        params SupervisorCandidate[] evidence
    ) => new(SupervisorResolutionStatus.Found, group, evidence, null);

    /// <summary>Constructs an evaluated origin that has no supervisor candidates.</summary>
    /// <param name="group">The evaluated own-membership origin group.</param>
    private static SupervisorResolution Missing(
        int group
    )
        => new(SupervisorResolutionStatus.NotFound, group, [], null);

    /// <summary>Builds one candidate occurrence with a complete source identity.</summary>
    /// <param name="group">The own-membership origin group.</param>
    /// <param name="person">The candidate user identity.</param>
    private static SupervisorCandidate Candidate(
        int group,
        int person
    ) => new()
    {
        TenantId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        SourceUserId = 42,
        StartGroupId = group,
        StartTreeId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        FoundGroupId = group - 1,
        CandidateUserId = person,
        Distance = 1,
        LeadershipPriority = 2,
    };

    /// <summary>Ensures an unsupported alternative is rejected explicitly.</summary>
    private sealed record UnknownSelection : AcrossOriginsSelection;
}
