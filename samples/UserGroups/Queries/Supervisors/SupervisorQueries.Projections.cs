using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{
    // WHY: These shapes contain only lambda parameters and literal values. Reuse their immutable
    // expression nodes on each read without retaining a request, context, model, or deferred query.

    /// <summary>Selects the exact membership group without capturing an invocation's request.</summary>
    private static readonly Expression<Func<UserGroupMembership, int>> s_membershipGroupKey =
        member => member.UserGroupId;

    /// <summary>Selects the mapped group identity shared by source and candidate joins.</summary>
    private static readonly Expression<Func<UserGroup, int>> s_groupKey = group => group.Id;

    /// <summary>Selects one authorized origin's group identity.</summary>
    private static readonly Expression<Func<SupervisorOrigin, int>> s_originGroupKey = origin => origin.Group.Id;

    /// <summary>Keeps both exact mapped aliases in an authorized source origin.</summary>
    private static readonly Expression<Func<UserGroupMembership, UserGroup, SupervisorOrigin>> s_originProjection =
    (
        member,
        group
    ) => new SupervisorOrigin
    {
        Membership = member,
        Group = group
    };

    /// <summary>Retains original mapped endpoints and their structural distance for ancestor composition.</summary>
    private static readonly Expression<Func<UserGroup, UserGroup, int, SupervisorGroupPath>> s_groupPathProjection =
    (
        start,
        found,
        distance
    ) => new SupervisorGroupPath
    {
        StartGroup = start,
        FoundGroup = found,
        Distance = distance,
    };

    /// <summary>Selects the scalar candidate after complete path and routing validation.</summary>
    private static readonly Expression<Func<SupervisorCandidatePath, SupervisorCandidate>> s_candidateProjection =
        pair => pair.Candidate;

    /// <summary>Selects an occurrence's group position before the distinct group window.</summary>
    private static readonly Expression<Func<SupervisorCandidate, SupervisorGroupPosition>> s_groupPositionProjection =
        row => new SupervisorGroupPosition
        {
            StartGroupId = row.StartGroupId,
            FoundGroupId = row.FoundGroupId,
            Distance = row.Distance,
        };

    /// <summary>Partitions distinct-person evidence by its exact source origin.</summary>
    private static readonly Expression<Func<SupervisorCandidate, int>> s_candidateOriginKey = row => row.StartGroupId;

    /// <summary>Preserves the complete scalar witness without rebuilding its five aggregate expressions.</summary>
    private static readonly Expression<Func<IGrouping<int, SupervisorCandidate>, SupervisorPersonWitness>>
        s_personWitnessProjection = group => new SupervisorPersonWitness
        {
            StartGroupId = group.Key,
            Minimum = group.Min(row => row.CandidateUserId),
            Maximum = group.Max(row => row.CandidateUserId),
            FoundGroupId = group.Min(row => row.FoundGroupId),
            Distance = group.Min(row => row.Distance),
            LeadershipPriority = group.Min(row => row.LeadershipPriority),
        };

    /// <summary>Projects complete evidence directly from the already authorized candidate occurrence.</summary>
    private static readonly Expression<Func<SupervisorCandidate, SupervisorReadRow>> s_evidenceRowProjection =
        candidate => new SupervisorReadRow
        {
            Kind = 1,
            HasEvidence = true,
            CatalogValid = false,
            StateOrdinal = 0,
            Count = 1,
            SourceUserId = candidate.SourceUserId,
            StartGroupId = candidate.StartGroupId,
            StartTreeId = candidate.StartTreeId,
            FoundGroupId = candidate.FoundGroupId,
            CandidateUserId = candidate.CandidateUserId,
            Distance = candidate.Distance,
            LeadershipPriority = candidate.LeadershipPriority,
        };

    /// <summary>Projects a marker from its exact authorized membership, including an empty search result.</summary>
    private static readonly Expression<Func<SupervisorOrigin, SupervisorReadRow>> s_originMarkerProjection = origin =>
        new SupervisorReadRow
        {
            Kind = 1,
            HasEvidence = false,
            CatalogValid = false,
            StateOrdinal = 0,
            Count = 0,
            SourceUserId = origin.Membership.UserId,
            StartGroupId = origin.Group.Id,
            StartTreeId = origin.Group.TreeId,
            FoundGroupId = 0,
            CandidateUserId = 0,
            Distance = 0,
            LeadershipPriority = 0,
        };

    /// <summary>Preserves every source status and the unique occurrence proven by a single-group rule.</summary>
    private static readonly Expression<Func<SupervisorOriginOutcome, SupervisorReadRow>> s_singleGroupRowProjection =
        outcome => new SupervisorReadRow
        {
            Kind = 1,
            CatalogValid = false,
            StateOrdinal = 0,
            HasEvidence = outcome.PersonId != null,
            Count = outcome.Count,
            SourceUserId = outcome.Origin.Membership.UserId,
            StartGroupId = outcome.Origin.Group.Id,
            StartTreeId = outcome.Origin.Group.TreeId,
            CandidateUserId = outcome.PersonId ?? 0,
            FoundGroupId = outcome.FoundGroupId ?? 0,
            Distance = outcome.Distance ?? 0,
            LeadershipPriority = outcome.LeadershipPriority ?? 0,
        };
}
