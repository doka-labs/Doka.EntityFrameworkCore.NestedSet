namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Marks one hierarchy role using either supported public manual-tracking mechanism.</summary>
    private static void MarkManualHierarchyChange(
        DbContext context,
        OrderingNode node,
        bool parent,
        bool currentValue
    )
    {
        if (parent)
        {
            var property = context
                .Entry(node)
                .Property(value => value.ParentId);

            if (currentValue)
            {
                property.CurrentValue = 2;
            }
            else
            {
                node.ParentId = 2;
                property.IsModified = true;
            }
        }
        else
        {
            var property = context
                .Entry(node)
                .Property(value => value.Name);

            if (currentValue)
            {
                property.CurrentValue = "Zulu";
            }
            else
            {
                node.Name = "Zulu";
                property.IsModified = true;
            }
        }
    }
}
