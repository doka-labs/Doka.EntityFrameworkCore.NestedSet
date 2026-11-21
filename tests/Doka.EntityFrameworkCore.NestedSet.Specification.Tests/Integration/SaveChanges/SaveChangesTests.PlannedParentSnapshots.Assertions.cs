namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Compares CLR list order exactly and EF relationship collections by their reference-set contract.
    /// </summary>
    private static void AssertPlannedParentState(
        PlannedEntryState[] expected,
        PlannedEntryState[] actual
    )
    {
        Assert.Equal(expected.Length, actual.Length);

        // WHY: Both snapshots are sorted identically, so compare each entity with its exact counterpart.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < expected.Length; index++)
        {
            var left = expected[index];
            var right = actual[index];
            Assert.Same(left.Entity, right.Entity);
            Assert.Equal(left.State, right.State);
            Assert.Equal(left.Properties, right.Properties);
            Assert.Equal(left.Navigations.Length, right.Navigations.Length);
            Assert.Equal(left.Principals, right.Principals);
            Assert.Equal(left.Dependents.Length, right.Dependents.Length);

            // WHY: The same metadata position identifies the navigation in each paired snapshot.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var navigation = 0; navigation < left.Navigations.Length; navigation++)
            {
                var before = left.Navigations[navigation];
                var after = right.Navigations[navigation];
                Assert.Equal(before.Name, after.Name);
                Assert.Same(before.CurrentReference, after.CurrentReference);
                Assert.Same(before.RelationshipReference, after.RelationshipReference);
                Assert.Equal(before.CurrentMembers, after.CurrentMembers, ReferenceEqualityComparer.Instance);
                Assert.True(
                    before
                        .RelationshipMembers
                        .ToHashSet(ReferenceEqualityComparer.Instance)
                        .SetEquals(after.RelationshipMembers));
            }

            // WHY: Native dependent maps are unordered; reference membership must survive rollback exactly.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var foreignKey = 0; foreignKey < left.Dependents.Length; foreignKey++)
            {
                Assert.Same(left.Dependents[foreignKey].ForeignKey, right.Dependents[foreignKey].ForeignKey);
                Assert.True(
                    left
                        .Dependents[foreignKey]
                        .Entries
                        .ToHashSet(ReferenceEqualityComparer.Instance)
                        .SetEquals(right.Dependents[foreignKey].Entries));
            }
        }
    }
}
