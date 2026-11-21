namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class InheritanceTests
{
    /// <summary>Checks current and original structure for every tracked derived node after bulk updates.</summary>
    private static void AssertTracked(
        DbContext context,
        InheritanceNode[] tracked,
        InheritanceNode[] persisted
    )
    {
        Assert.Equal(
            persisted
                .OrderBy(node => node.Id)
                .Select(Snapshot),
            tracked
                .OrderBy(node => node.Id)
                .Select(Snapshot));

        foreach (var node in tracked)
        {
            var entry = context.Entry(node);
            Assert.Equal(
                node.TreeId,
                entry.Property(value => value.TreeId)
                    .OriginalValue);

            Assert.Equal(
                node.ParentId,
                entry.Property(value => value.ParentId)
                    .OriginalValue);

            Assert.Equal(
                node.Left,
                entry.Property(value => value.Left)
                    .OriginalValue);

            Assert.Equal(
                node.Right,
                entry.Property(value => value.Right)
                    .OriginalValue);

            Assert.Equal(
                node.Depth,
                entry.Property(value => value.Depth)
                    .OriginalValue);

            Assert.Equal(
                node.Position,
                entry.Property(value => value.Position)
                    .OriginalValue);

            Assert.False(
                entry.Property(value => value.ParentId)
                    .IsModified);

            Assert.False(
                entry.Property(value => value.Left)
                    .IsModified);

            Assert.False(
                entry.Property(value => value.Right)
                    .IsModified);

            Assert.False(
                entry.Property(value => value.Depth)
                    .IsModified);

            Assert.False(
                entry.Property(value => value.Position)
                    .IsModified);
        }
    }
}
