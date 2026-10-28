namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>
/// Validates the finite sample catalog before ordinary EF writes; database FKs still enforce tenant and owner identity.
/// </summary>
internal static class SupervisorPayloadValidation
{
    /// <summary>
    /// Rejects unknown fields, unsupported scalar kinds, and non-ASCII routing data before database execution.
    /// </summary>
    internal static void Validate(
        UserGroupContext context
    )
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case UserMetadataDefinition definition:
                    Definition(definition.FieldCode, definition.ValueKind);
                    break;
                case MembershipMetadataDefinition definition:
                    Definition(definition.FieldCode, definition.ValueKind);
                    break;
                case GroupMetadataDefinition definition:
                    Definition(definition.FieldCode, definition.ValueKind);
                    break;
                case UserMetadata value:
                    Scalar(value.FieldCode, value.Value);
                    break;
                case MembershipMetadata value:
                    Scalar(value.FieldCode, value.Value);
                    break;
                case GroupMetadata value:
                    Scalar(value.FieldCode, value.Value);
                    break;
                case GroupRelationPeriod period when period.ToDate < period.FromDate:
                    throw new ArgumentException(
                        "A direct-edge validity period cannot end before its inclusive first day.");
                case UserGroupMembership member when member.ToDate < member.FromDate:
                    throw new ArgumentException(
                        "A membership validity period cannot end before its inclusive first day.");
                case MembershipRole role when role.ToDate < role.FromDate:
                    throw new ArgumentException(
                        "A local role validity period cannot end before its inclusive first day.");
                case UserRole role when role.ToDate < role.FromDate:
                    throw new ArgumentException(
                        "A global role validity period cannot end before its inclusive first day.");
            }
        }
    }

    /// <summary>
    /// Retains a finite explicit Text catalog instead of silently accepting incompatible field definitions.
    /// </summary>
    private static void Definition(
        string code,
        string kind
    )
    {
        if (!SupervisorRequestValidation.IsKnownField(code)
            || kind != "Text")
        {
            throw new ArgumentException("The sample catalog permits only the documented Text fields.");
        }
    }

    /// <summary>Keeps opaque values unnormalized; missing values remain representable but never match.</summary>
    private static void Scalar(
        string code,
        string? value
    )
    {
        if (!SupervisorRequestValidation.IsKnownField(code)
            || (value is not null && value.Any(character => !char.IsAscii(character))))
        {
            throw new ArgumentException("Routing values use documented fields and unnormalized ASCII text.");
        }
    }
}
