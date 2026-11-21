namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

internal static partial class NestedSetModelValidator
{
    /// <summary>Requires one distinct mapped property per structural role.</summary>
    private static void RequireDistinctRoles(
        IConventionEntityType entity,
        IConventionProperty[] roles
    )
    {
        if (roles.Distinct().Count() != roles.Length)
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires a different property for every structural role.");
        }
    }

    /// <summary>Validates required identities and the nullable direct-parent role.</summary>
    private static void ValidateIdentityRoles(
        IConventionEntityType entity,
        IConventionProperty nodeKey,
        IConventionProperty? scope,
        IConventionProperty treeId,
        IConventionProperty parent
    )
    {
        ValidateIdentity(entity, nodeKey, scope);
        Required(nodeKey, entity, NestedSetPropertyRoles.NodeKey);
        Required(treeId, entity, NestedSetPropertyRoles.TreeId);
        Writable(treeId, entity, NestedSetPropertyRoles.TreeId);

        if (scope is not null)
        {
            Required(scope, entity, NestedSetPropertyRoles.Scope);
            AssignableOnInsert(scope, entity, NestedSetPropertyRoles.Scope);
        }

        if (!parent.IsNullable || ModelType(parent) != ModelType(nodeKey))
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires a nullable Parent property with the NodeKey model type.");
        }
    }

    /// <summary>Keeps coordinate arithmetic on the exact required unconverted integer types.</summary>
    private static void ValidateArithmeticRoles(
        IConventionEntityType entity,
        IConventionProperty left,
        IConventionProperty right,
        IConventionProperty depth,
        IConventionProperty position
    )
    {
        RequiredType(left, typeof(long), entity, NestedSetPropertyRoles.Left);
        RequiredType(right, typeof(long), entity, NestedSetPropertyRoles.Right);
        RequiredType(depth, typeof(int), entity, NestedSetPropertyRoles.Depth);
        RequiredType(position, typeof(long), entity, NestedSetPropertyRoles.Position);
        RequireUnconvertedArithmetic(left, entity, NestedSetPropertyRoles.Left);
        RequireUnconvertedArithmetic(right, entity, NestedSetPropertyRoles.Right);
        RequireUnconvertedArithmetic(depth, entity, NestedSetPropertyRoles.Depth);
        RequireUnconvertedArithmetic(position, entity, NestedSetPropertyRoles.Position);
    }

    /// <summary>Rejects generated or otherwise unwritable structural fields after validating their types.</summary>
    private static void ValidateRoleWrites(
        IConventionEntityType entity,
        IConventionProperty parent,
        IConventionProperty left,
        IConventionProperty right,
        IConventionProperty depth,
        IConventionProperty position
    )
    {
        foreach (var (property, role) in new[]
            {
                (parent, NestedSetPropertyRoles.Parent),
                (left, NestedSetPropertyRoles.Left),
                (right, NestedSetPropertyRoles.Right),
                (depth, NestedSetPropertyRoles.Depth),
                (position, NestedSetPropertyRoles.Position),
            })
        {
            Writable(property, entity, role);
        }
    }
}
