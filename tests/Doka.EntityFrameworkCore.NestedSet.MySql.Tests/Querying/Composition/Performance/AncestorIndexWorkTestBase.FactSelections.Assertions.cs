using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class AncestorIndexWorkTestBase
{

    /// <summary>Applies deterministic native work and complete occurrence contracts.</summary>
    private void AssertConsumerFacts(
        ConsumerFactObservation observation,
        int sourceCount
    )
    {
        var tuples = Math.Max(
            observation.Work.OperatorTuples,
            Math.Max(
                observation.Traversals / (double)observation.Reads,
                observation.IndexAttempts / (double)observation.Reads));

        // WHY: The typed adapter requires the qualified binding and ordered join. Work must scale
        // with selected source occurrences and result rows, even as the stored population grows.
        var budget = 64L * (sourceCount + observation.Expected.Length + 1);

        _output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{observation.Profile}; population={observation.Population}; "
                + $"boundedPathRequired={observation.BoundedPathRequired}; "
                + $"actualRows={observation.Actual.Length}; "
                + $"nativeTuples={tuples}; budget={budget}."));

        Assert.True(observation.BoundedPathRequired);
        Assert.Equal(observation.Expected, observation.Actual);
        Assert.Equal(0, observation.ConstructionCommands);
        Assert.Equal(observation.Reads, observation.Commands);
        Assert.Equal(0, observation.Tracked);
        Assert.True(observation.Work.ExecutedOperators > 0);
        Assert.InRange(tuples, 0, budget);
    }
}
