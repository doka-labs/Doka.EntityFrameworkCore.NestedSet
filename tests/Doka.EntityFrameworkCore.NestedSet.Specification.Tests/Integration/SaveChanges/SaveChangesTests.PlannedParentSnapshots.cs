namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Orders each fixture FK by its declared mapping and scalar names, without reading entity values.
    /// </summary>
    private static string ForeignKeyOrder(
        IForeignKey foreignKey
    )
        => foreignKey.DeclaringEntityType.Name
            + ":"
            + foreignKey.PrincipalEntityType.Name
            + ":"
            + string.Join(",", foreignKey.Properties.Select(property => property.Name));

    /// <summary>
    ///     Freezes collection references so a later retry cannot change the captured failed-state evidence.
    /// </summary>
    private static object[] CopyPlannedMembers(
        object? value
    ) => value is IEnumerable<object> members
        ? members.ToArray()
        : [];
}
