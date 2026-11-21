namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

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

        // WHY: Request validation preserves the first invalid condition in the indexable input order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var matchIndex = 0; matchIndex < request.Matches.Count; matchIndex++)
        {
            var match = request.Matches[matchIndex];

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
