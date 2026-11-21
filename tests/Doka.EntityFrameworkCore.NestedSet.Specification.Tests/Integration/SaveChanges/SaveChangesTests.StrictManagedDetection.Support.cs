namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Creates detached inputs for one single insert, one subtree, or two forest roots.</summary>
    private static OrderingNode[] ManualInsertionInputs(
        string shape
    ) => shape switch
    {
        "single" =>
        [
            new OrderingNode
            {
                Id = 101,
                Name = "Inserted",
            },
        ],
        "subtree" =>
        [
            new OrderingNode
            {
                Id = 101,
                Name = "Inserted",
            },
            new OrderingNode
            {
                Id = 102,
                Name = "Child",
            },
        ],
        "forest" =>
        [
            new OrderingNode
            {
                Id = 101,
                Name = "Inserted",
            },
            new OrderingNode
            {
                Id = 102,
                Name = "Child",
            },
            new OrderingNode
            {
                Id = 201,
                Name = "Second",
            },
            new OrderingNode
            {
                Id = 202,
                Name = "Child",
            },
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };
}
