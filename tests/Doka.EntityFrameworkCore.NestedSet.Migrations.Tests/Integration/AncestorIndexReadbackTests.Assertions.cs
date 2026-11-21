namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexReadbackTests
{
    /// <summary>Checks the actual generated sidecars before a later refresh could repair wrong attribution.</summary>
    private static void AssertReadback(
        ReadbackObservation observation,
        AncestorIndexReadbackNode[] nodes,
        int inputIncrement
    )
    {
        Assert.True(observation.PayloadSaves >= 2);
        Assert.Equal(nodes.Length, observation.SavedRows.Count);
        Assert.Equal(
            nodes.Length,
            observation
                .SavedRows
                .Select(row => row.Id)
                .Distinct()
                .Count());

        Assert.All(
            observation.SavedRows,
            row =>
            {
                Assert.Equal(row.Input * 17L + inputIncrement * 17L + 3L, row.Stamp);
                Assert.Equal(row.Id == nodes[0].Id, row.IsBranch);
            });
    }

    /// <summary>Checks the expected failure type without accepting an unrelated rollback error.</summary>
    private void AssertFailure(
        Exception? error,
        bool cancel
    )
    {
        _output.WriteLine("Observed import failure: " + error);

        if (cancel)
        {
            Assert.IsType<OperationCanceledException>(error, exactMatch: false);

            return;
        }

        var failure = Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(ReadbackObservation.FailureMessage, failure.Message);
    }

    /// <summary>
    /// Validates the observed native format without making an optional provider optimization mandatory.
    /// </summary>
    private void AssertNativeShape(
        string engine,
        ReadbackObservation observation,
        bool declaredTrigger
    )
    {
        var inserts = observation.Commands.Sum(command => CountOccurrences(command, "INSERT INTO"));
        var unions = observation.Commands.Sum(command => CountOccurrences(command, "UNION ALL"));
        var returning = observation.Commands.Sum(command => CountOccurrences(command, "RETURNING"));
        _output.WriteLine(
            $"Engine={engine}; rows={observation.SavedRows.Count}; payloadSaves={observation.PayloadSaves}; "
            + $"commands={observation.Commands.Count}; inserts={inserts}; unions={unions}; returning={returning}.");

        Assert.NotEmpty(observation.Commands);
        Assert.InRange(inserts, 1, ImportCount);

        if (engine == "MariaDb")
        {
            Assert.Equal(inserts, returning);
            Assert.Equal(0, unions);
        }
        else
        {
            Assert.Equal(0, returning);

            // WHY: Published Doka retains a correct per-row fallback. The final qualification separately proves
            // selection of its newer grouped path from these real counts, without coupling unrelated API markers.
            Assert.Equal(
                unions == 0
                    ? ImportCount
                    : ImportCount - unions,
                inserts);
        }

        if (declaredTrigger)
        {
            Assert.Equal(ImportCount, inserts);
            Assert.Equal(0, unions);
        }

        _output.WriteLine("First native readback command: " + observation.Commands[0]);
    }

    /// <summary>Counts actual statement markers without changing the command sent to the server.</summary>
    private static int CountOccurrences(
        string text,
        string marker
    )
    {
        var count = 0;
        var offset = 0;

        while ((offset = text.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }

        return count;
    }
}
