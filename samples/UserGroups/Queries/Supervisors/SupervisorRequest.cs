namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Selects whether the source membership group itself can supply a supervisor.</summary>
internal enum SearchStart
{
    /// <summary>Includes a qualifying person in the source membership's own group.</summary>
    IncludeSelf,

    /// <summary>Searches only groups strictly above the source membership.</summary>
    ParentsOnly,
}

/// <summary>Identifies the exact source or candidate owner of a routing value.</summary>
internal enum MetadataOwner
{
    /// <summary>The explicitly retained routing user's metadata.</summary>
    SourceUser,

    /// <summary>The routing user's exact origin membership metadata.</summary>
    SourceMembership,

    /// <summary>The routing origin group's metadata.</summary>
    SourceGroup,

    /// <summary>The candidate person's metadata.</summary>
    CandidateUser,

    /// <summary>The candidate's exact found membership metadata.</summary>
    CandidateMembership,

    /// <summary>The found group's metadata.</summary>
    CandidateGroup,
}

/// <summary>Names a permitted pair of scalar routing fields on explicit owners.</summary>
/// <param name="SourceOwner">The initial or current routing owner.</param>
/// <param name="SourceFieldCode">The source catalog field.</param>
/// <param name="CandidateOwner">The exact candidate owner.</param>
/// <param name="CandidateFieldCode">The candidate catalog field.</param>
internal sealed record MetadataMatch(
    MetadataOwner SourceOwner,
    string SourceFieldCode,
    MetadataOwner CandidateOwner,
    string CandidateFieldCode
);

/// <summary>Requires an explicit choice of a person-role source.</summary>
internal abstract record SupervisorRoles;

/// <summary>Explicitly applies no person-role filter.</summary>
internal sealed record NoRoleFilter : SupervisorRoles;

/// <summary>Requires a role on the exact candidate membership.</summary>
/// <param name="RoleId">The tenant-owned role.</param>
internal sealed record MembershipRoleFilter(Guid RoleId) : SupervisorRoles;

/// <summary>Requires the candidate user's tenant-wide role.</summary>
/// <param name="RoleId">The tenant-owned role.</param>
internal sealed record GlobalUserRole(Guid RoleId) : SupervisorRoles;

/// <summary>Accepts the exact local role or the candidate user's global role without duplicating people.</summary>
/// <param name="RoleId">The tenant-owned role.</param>
internal sealed record EitherRole(Guid RoleId) : SupervisorRoles;

/// <summary>Requires an explicit structural group-selection rule.</summary>
internal abstract record SupervisorGroups;

/// <summary>Selects every group satisfying the complete candidate rule; an empty type list allows all types.</summary>
/// <param name="Types">Permitted application type identities.</param>
internal sealed record AllEligibleGroups(params int[] Types) : SupervisorGroups;

/// <summary>Selects the one-based qualifying group separately for each origin.</summary>
/// <param name="Types">Permitted application type identities; empty allows all.</param>
/// <param name="Number">The requested one-based group rank.</param>
internal sealed record NthEligibleGroup(
    int[] Types,
    int Number
) : SupervisorGroups;

/// <summary>Ranks groups of one type before candidate or path validation, without a farther fallback.</summary>
/// <param name="TypeId">The required application group type.</param>
/// <param name="Number">The one-based type-group rank.</param>
internal sealed record NthTypeGroupThenValidate(
    int TypeId,
    int Number
) : SupervisorGroups;

/// <summary>Contains the explicit rules for one deferred, tenant-bound supervisor search.</summary>
internal sealed record SupervisorRequest
{
    /// <summary>Gets the source identity without requiring an entity load.</summary>
    internal required int SourceUserId { get; init; }

    /// <summary>Gets an optional exact own membership; null selects all visible own memberships.</summary>
    internal int? StartGroupId { get; init; }

    /// <summary>Gets the explicit lower search boundary.</summary>
    internal required SearchStart SearchStart { get; init; }

    /// <summary>Gets the explicit person-role rule.</summary>
    internal required SupervisorRoles Roles { get; init; }

    /// <summary>Gets the explicit group-ranking rule.</summary>
    internal required SupervisorGroups Groups { get; init; }

    /// <summary>Gets same-candidate routing conditions combined with AND.</summary>
    internal required IReadOnlyList<MetadataMatch> Matches { get; init; }

    /// <summary>Gets the inclusive validity date; null does not infer today's date.</summary>
    internal DateOnly? OnDay { get; init; }

    /// <summary>Gets the routing source override used only for explicitly retained initial-subject routing.</summary>
    internal int? RoutingUserId { get; init; }

    /// <summary>Gets the exact routing membership override used by initial-subject chains.</summary>
    internal int? RoutingGroupId { get; init; }
}

/// <summary>Validates application request syntax before constructing any provider query.</summary>
internal static class SupervisorRequestValidation
{
    /// <summary>
    /// Rejects unknown alternatives, invalid ranks, and invalid field owners without database access.
    /// </summary>
    internal static void Validate(
        SupervisorRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Matches);
        if (!Enum.IsDefined(request.SearchStart))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        switch (request.Roles)
        {
            case NoRoleFilter:
                break;
            case MembershipRoleFilter { RoleId: var id } when id != Guid.Empty:
            case GlobalUserRole { RoleId: var id2 } when id2 != Guid.Empty:
            case EitherRole { RoleId: var id3 } when id3 != Guid.Empty:
                break;
            default:
                throw new ArgumentException(
                    "Choose an explicit person-role rule with a nonempty role identity.",
                    nameof(request));
        }

        switch (request.Groups)
        {
            case AllEligibleGroups { Types: not null }:
                break;
            case NthEligibleGroup { Types: not null, Number: > 0 }:
                break;
            case NthTypeGroupThenValidate { Number: > 0 }:
                break;
            default:
                throw new ArgumentException(
                    "Choose an explicit group rule with a positive one-based rank.",
                    nameof(request));
        }

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var match in request.Matches)
        {
            if (match is null
                || match.SourceOwner is not (MetadataOwner.SourceUser
                    or MetadataOwner.SourceMembership
                    or MetadataOwner.SourceGroup)
                || match.CandidateOwner is not (MetadataOwner.CandidateUser
                    or MetadataOwner.CandidateMembership
                    or MetadataOwner.CandidateGroup)
                || !IsKnownField(match.SourceFieldCode)
                || !IsKnownField(match.CandidateFieldCode))
            {
                throw new ArgumentException(
                    "Routing requires known Text fields and explicit source/candidate owners.",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Keeps this runnable example's catalog deliberately finite rather than introducing a metadata framework.
    /// </summary>
    internal static bool IsKnownField(
        string code
    ) => code is "Route"
        or "ChannelClass"
        or "ManagerRoute"
        or "RoutingZone"
        or "Lane"
        or "Cluster"
        or "Protocol"
        or "Mode"
        or "Profile"
        or "Phase"
        or "Zone"
        or "Port"
        or "Rack"
        or "Batch"
        or "Firmware"
        or "DeviceProfile";
}
