namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Checks exact public complex state without treating record-array references as value equality.</summary>
    private static void AssertComplexCallbackNative(
        ComplexCallbackNativeState[] before,
        ComplexCallbackNativeState[] after
    )
    {
        Assert.Equal(before.Length, after.Length);

        // WHY: Capture order is stable; corresponding ordinals must retain identity, flags and original snapshots.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Length; index++)
        {
            Assert.Equal(before[index].Path, after[index].Path);
            Assert.Same(before[index].Instance, after[index].Instance);
            Assert.Equal(before[index].State, after[index].State);
            Assert.Equal(before[index].Modified, after[index].Modified);
            Assert.Equal(before[index].Leaves, after[index].Leaves);
            Assert.Equal(before[index].Originals, after[index].Originals);
        }
    }

    /// <summary>Checks original containers, element identities and order together with copied raw values.</summary>
    private static void AssertComplexCallbackClr(
        ComplexCallbackClrState before,
        ComplexCallbackClrState after
    )
    {
        Assert.Same(before.Row, after.Row);
        Assert.Same(before.Items, after.Items);
        Assert.Same(before.Address, after.Address);
        Assert.Equal(before.AddressText, after.AddressText);
        Assert.Same(before.Tags, after.Tags);
        Assert.Equal(before.TagValues, after.TagValues);
        Assert.Equal(before.ItemValues.Length, after.ItemValues.Length);

        // WHY: Each caller-owned element and nested list must survive in its original current position.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.ItemValues.Length; index++)
        {
            var expected = before.ItemValues[index];
            var actual = after.ItemValues[index];
            Assert.Same(expected.Item, actual.Item);
            Assert.Equal(expected.Text, actual.Text);
            Assert.Same(expected.Parts, actual.Parts);
            Assert.Equal(expected.PartValues, actual.PartValues);
        }
    }
}
