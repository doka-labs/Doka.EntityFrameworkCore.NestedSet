namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Exposes deferred candidates, scalar resolution, and explicitly different supervisor-chain workflows.</summary>
internal static partial class SupervisorQueries
{
    /// <summary>Builds visible supervisor candidates while preserving each source membership path.</summary>
    /// <param name="context">The single context supplying all query roots.</param>
    /// <param name="tenantId">The authorized tenant bound to every source.</param>
    /// <param name="request">The explicit search, role, group, and routing rules.</param>
    /// <returns>An unordered deferred query; construction performs no I/O.</returns>
    internal static IQueryable<SupervisorCandidate> Candidates(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        SupervisorRequestValidation.Validate(request);
        var memberships = SourceMemberships(context, tenantId, request);
        var origins = Origins(context, tenantId, request.SourceUserId, memberships);

        return BuildCandidates(context, tenantId, request, origins, memberships);
    }

    /// <summary>Resolves each source membership with one asynchronous status-and-evidence reader.</summary>
    /// <param name="context">The single context supplying all query roots.</param>
    /// <param name="tenantId">The authorized tenant.</param>
    /// <param name="request">The explicit candidate rules.</param>
    /// <param name="selection">The explicit person-selection contract.</param>
    /// <param name="cancellationToken">The token forwarded to asynchronous execution.</param>
    /// <returns>Complete per-origin missing, unique, or ambiguous outcomes.</returns>
    internal static Task<SupervisorResolutionBatch> ResolveAsync(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        SupervisorSelection selection,
        CancellationToken cancellationToken
    ) => ResolveCoreAsync(context, tenantId, request, selection, cancellationToken);

    /// <summary>Reduces a complete resolved batch without performing additional I/O.</summary>
    /// <param name="batch">The materialized request result including every visible origin.</param>
    /// <param name="selection">The explicit cross-origin uniqueness or priority rule.</param>
    /// <returns>An explicit global person outcome retaining all supporting paths.</returns>
    internal static SupervisorResolution SelectAcrossOrigins(
        SupervisorResolutionBatch batch,
        AcrossOriginsSelection selection
    ) => SupervisorSelectionRules.Across(batch, selection);

    /// <summary>Follows supervisors strictly above the previous found group on every original path.</summary>
    /// <param name="context">The context enlisted in the qualified read snapshot.</param>
    /// <param name="tenantId">The authorized tenant for every step.</param>
    /// <param name="request">Finite steps, routing policy, and mandatory safety bounds.</param>
    /// <param name="cancellationToken">The token forwarded to every database operation.</param>
    /// <returns>Provenance-preserving hops and explicit branch outcomes.</returns>
    internal static Task<SupervisorChainResult> FollowOriginalPathAsync(
        UserGroupContext context,
        Guid tenantId,
        SupervisorChainRequest request,
        CancellationToken cancellationToken
    )
    {
        request.Validate(originalPath: true);

        return SampleReadSnapshot.RunAsync(
            context,
            token => FollowAsync(context, tenantId, request, true, token),
            cancellationToken);
    }

    /// <summary>
    /// Re-expands the selected person's visible memberships at each hop, explicitly allowing new own trees.
    /// </summary>
    /// <param name="context">The context enlisted in the qualified read snapshot.</param>
    /// <param name="tenantId">The authorized tenant, retained across every expansion.</param>
    /// <param name="request">Finite steps, routing policy, and mandatory safety bounds.</param>
    /// <param name="cancellationToken">The token forwarded to every database operation.</param>
    /// <returns>Branch-preserving hops and explicit ambiguity, cycle, or budget outcomes.</returns>
    internal static Task<SupervisorChainResult> FollowAllMembershipsAsync(
        UserGroupContext context,
        Guid tenantId,
        SupervisorChainRequest request,
        CancellationToken cancellationToken
    )
    {
        request.Validate(originalPath: false);

        return SampleReadSnapshot.RunAsync(
            context,
            token => FollowAsync(context, tenantId, request, false, token),
            cancellationToken);
    }
}
