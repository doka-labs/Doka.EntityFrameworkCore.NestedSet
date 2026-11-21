namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{

    /// <summary>
    /// Reuses one deferred owner projection across same-owner criteria instead of rebuilding its catalog predicates.
    /// </summary>
    private static IQueryable<SupervisorRoutingValue> SharedRoutingValues(
        UserGroupContext context,
        Guid tenantId,
        MetadataOwner owner,
        IQueryable<SupervisorRoutingValue>?[] sources
    )
    {
        var index = owner switch
        {
            MetadataOwner.SourceUser or MetadataOwner.CandidateUser => 0,
            MetadataOwner.SourceMembership or MetadataOwner.CandidateMembership => 1,
            MetadataOwner.SourceGroup or MetadataOwner.CandidateGroup => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };

        return sources[index] ??= RoutingValues(context, owner)
            .Where(value => value.TenantId == tenantId);
    }

    /// <summary>Normalizes only the three explicitly chosen owner sources into scalar query projections.</summary>
    private static IQueryable<SupervisorRoutingValue> RoutingValues(
        UserGroupContext context,
        MetadataOwner owner
    )
    {
        // WHY: Owner-specific tables keep physical FKs; this shape sharing stays entirely in server expressions.
        var values = owner switch
        {
            MetadataOwner.SourceUser or MetadataOwner.CandidateUser => context
                .UserMetadatas
                .Where(value => context.UserMetadataDefinitions.Any(definition => definition.TenantId == value.TenantId

                    // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                    // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                    && SupervisorExactText.FieldEquals(definition.FieldCode, value.FieldCode)

                    // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                    // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                    && SupervisorExactText.FieldEquals(definition.ValueKind, "Text")))
                .Select(value => new SupervisorRoutingValue
                {
                    TenantId = value.TenantId,
                    UserId = value.UserId,
                    GroupId = 0,
                    FieldCode = value.FieldCode,
                    Value = value.Value,
                }),
            MetadataOwner.SourceMembership or MetadataOwner.CandidateMembership => context
                .MembershipMetadatas
                .Where(value =>
                    context.Groups.Any(group =>
                        group.TenantId == value.TenantId
                        && group.Id == value.UserGroupId
                        && group.TypeId == value.GroupTypeId)
                    && context.MembershipMetadataDefinitions.Any(definition => definition.TenantId == value.TenantId
                        && definition.GroupTypeId == value.GroupTypeId

                        // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                        && SupervisorExactText.FieldEquals(definition.FieldCode, value.FieldCode)

                        // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                        && SupervisorExactText.FieldEquals(definition.ValueKind, "Text")))
                .Select(value => new SupervisorRoutingValue
                {
                    TenantId = value.TenantId,
                    UserId = value.UserId,
                    GroupId = value.UserGroupId,
                    FieldCode = value.FieldCode,
                    Value = value.Value,
                }),
            MetadataOwner.SourceGroup or MetadataOwner.CandidateGroup => context
                .GroupMetadatas
                .Where(value =>
                    context.Groups.Any(group =>
                        group.TenantId == value.TenantId
                        && group.Id == value.UserGroupId
                        && group.TypeId == value.GroupTypeId)
                    && context.GroupMetadataDefinitions.Any(definition => definition.TenantId == value.TenantId
                        && definition.GroupTypeId == value.GroupTypeId

                        // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                        && SupervisorExactText.FieldEquals(definition.FieldCode, value.FieldCode)

                        // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                        && SupervisorExactText.FieldEquals(definition.ValueKind, "Text")))
                .Select(value => new SupervisorRoutingValue
                {
                    TenantId = value.TenantId,
                    UserId = 0,
                    GroupId = value.UserGroupId,
                    FieldCode = value.FieldCode,
                    Value = value.Value,
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };

        return values.Provider.CreateQuery<SupervisorRoutingValue>(SupervisorExactText.Inline(values.Expression));
    }
}
