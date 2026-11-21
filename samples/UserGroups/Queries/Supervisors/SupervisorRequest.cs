namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

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
