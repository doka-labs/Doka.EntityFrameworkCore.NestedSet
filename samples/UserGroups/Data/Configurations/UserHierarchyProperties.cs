namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Names the structural columns kept in EF's shadow state instead of the User CLR type.</summary>
public static class UserHierarchyProperties
{
    /// <summary>The stable identity of the supervisor tree.</summary>
    public const string TreeId = "TreeId";

    /// <summary>The inclusive left boundary within the supervisor tree.</summary>
    public const string Left = "Left";

    /// <summary>The inclusive right boundary within the supervisor tree.</summary>
    public const string Right = "Right";

    /// <summary>The dense sibling order, distinct from an employee's business Position.</summary>
    public const string NestedSetPosition = "NestedSetPosition";
}
