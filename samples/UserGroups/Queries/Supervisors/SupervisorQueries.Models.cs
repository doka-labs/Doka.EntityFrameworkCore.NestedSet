namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{
    /// <summary>
    /// Retains only expression aliases until candidate filtering and complete-path validation are composed.
    /// </summary>
    private sealed class SupervisorCandidatePath
    {
        /// <summary>Gets the exact mapped source membership group.</summary>
        public required UserGroup StartGroup { get; init; }

        /// <summary>Gets the exact mapped candidate membership group.</summary>
        public required UserGroup FoundGroup { get; init; }

        /// <summary>Gets the scalar DTO projected after all server-side path filters.</summary>
        public required SupervisorCandidate Candidate { get; init; }
    }

    /// <summary>Identifies an actual group occurrence before reducing multiple matching people to one slot.</summary>
    private sealed class SupervisorGroupPosition
    {
        /// <summary>Gets the exact source membership group.</summary>
        public int StartGroupId { get; init; }

        /// <summary>Gets the actual structural or application-qualified group.</summary>
        public int FoundGroupId { get; init; }

        /// <summary>Gets its structural distance from the origin.</summary>
        public int Distance { get; init; }
    }

    /// <summary>Retains an occupied group ordinal without using a technical-key absence sentinel.</summary>
    private sealed class SupervisorGroupChoice
    {
        /// <summary>Gets the exact source membership group.</summary>
        public int StartGroupId { get; init; }

        /// <summary>Gets the selected group key without reserving zero or a negative value for absence.</summary>
        public int? FoundGroupId { get; init; }

        /// <summary>Gets the selected group's already-qualified structural distance.</summary>
        public int Distance { get; init; }
    }

    /// <summary>Retains mapped aliases until structural path validation is composed.</summary>
    private sealed class SupervisorGroupPath
    {
        /// <summary>Gets the selected source group alias.</summary>
        public required UserGroup StartGroup { get; init; }

        /// <summary>Gets the selected ancestor alias.</summary>
        public required UserGroup FoundGroup { get; init; }

        /// <summary>Gets the original structural edge distance.</summary>
        public int Distance { get; init; }
    }

    /// <summary>Keeps one exact membership and its visible mapped group in the deferred origin query.</summary>
    private sealed class SupervisorOrigin
    {
        /// <summary>Gets the source membership alias.</summary>
        public required UserGroupMembership Membership { get; init; }

        /// <summary>Gets the visible source group alias.</summary>
        public required UserGroup Group { get; init; }
    }

    /// <summary>Projects scalar owner values for one explicitly selected metadata table.</summary>
    private sealed class SupervisorRoutingValue
    {
        /// <summary>Gets the tenant.</summary>
        public Guid TenantId { get; init; }

        /// <summary>Gets the user owner, or zero when this source has no user owner.</summary>
        public int UserId { get; init; }

        /// <summary>Gets the group owner, or zero when this source has no group owner.</summary>
        public int GroupId { get; init; }

        /// <summary>Gets the defined scalar field.</summary>
        public required string FieldCode { get; init; }

        /// <summary>Gets the stored scalar value.</summary>
        public string? Value { get; init; }
    }

    /// <summary>Contains person extrema and the occurrence fields available under a single-group proof.</summary>
    private sealed class SupervisorPersonWitness
    {
        /// <summary>Gets the exact source membership group.</summary>
        public int StartGroupId { get; init; }

        /// <summary>Gets the smallest candidate identity, or SQL NULL for an absent aggregate.</summary>
        public int? Minimum { get; init; }

        /// <summary>Gets the largest candidate identity, or SQL NULL for an absent aggregate.</summary>
        public int? Maximum { get; init; }

        /// <summary>Gets the selected group's key when a group window proves one occurrence per person.</summary>
        public int? FoundGroupId { get; init; }

        /// <summary>Gets that selected group's structural distance within a single-group witness.</summary>
        public int? Distance { get; init; }

        /// <summary>Gets the selected person's priority when the witness proves one exact group occurrence.</summary>
        public int? LeadershipPriority { get; init; }
    }

    /// <summary>Retains one visible origin and its server-proven zero, one, or multiple-person outcome.</summary>
    private sealed class SupervisorOriginOutcome
    {
        /// <summary>Gets the exact source membership and mapped group.</summary>
        public required SupervisorOrigin Origin { get; init; }

        /// <summary>Gets the saturated distinct-person category: zero, one, or multiple.</summary>
        public long Count { get; init; }

        /// <summary>Gets the proven unique person, or null when no person can be admitted.</summary>
        public int? PersonId { get; init; }

        /// <summary>Gets the aggregate's found group for a single-group selection.</summary>
        public int? FoundGroupId { get; init; }

        /// <summary>Gets the aggregate's structural distance for a single-group selection.</summary>
        public int? Distance { get; init; }

        /// <summary>Gets the aggregate's selected membership priority for a single-group selection.</summary>
        public int? LeadershipPriority { get; init; }
    }

    /// <summary>Uses one fixed scalar shape for source/status markers and candidate evidence.</summary>
    internal sealed class SupervisorReadRow
    {
        /// <summary>Gets the row role: source marker or origin status/evidence.</summary>
        public int Kind { get; init; }

        /// <summary>Gets whether this row carries actual candidate evidence rather than an origin marker.</summary>
        public bool HasEvidence { get; init; }

        /// <summary>Gets whether the requested tenant catalog identity is visible.</summary>
        public bool CatalogValid { get; init; }

        /// <summary>Gets the frontier state ordinal when several searches share one reader.</summary>
        public int StateOrdinal { get; init; }

        /// <summary>Gets the source user identity.</summary>
        public int SourceUserId { get; init; }

        /// <summary>Gets the source membership group.</summary>
        public int StartGroupId { get; init; }

        /// <summary>Gets its stable tree identity.</summary>
        public Guid StartTreeId { get; init; }

        /// <summary>Gets the saturated server-reduced distinct-person category: zero, one, or multiple.</summary>
        public long Count { get; init; }

        /// <summary>Gets the qualifying membership group.</summary>
        public int FoundGroupId { get; init; }

        /// <summary>Gets the selected person identity.</summary>
        public int CandidateUserId { get; init; }

        /// <summary>Gets the structural distance.</summary>
        public int Distance { get; init; }

        /// <summary>Gets the application leadership priority.</summary>
        public int LeadershipPriority { get; init; }
    }
}
