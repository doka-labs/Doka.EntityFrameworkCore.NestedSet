namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Persists asymmetric nested counts so shifting outer ordinals cannot hide stale child lanes.</summary>
    private async Task<TestDatabase> CreateComplexCompositionDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = await CreateComplexCallbackDatabaseAsync(cancellationToken: cancellationToken);

        try
        {
            await using var context = await CreateComplexCallbackContextAsync(database,
                cancellationToken: cancellationToken);

            var row = await context.Set<ComplexCallbackRow>().SingleAsync(cancellationToken);
            row.Items =
            [
                NewComplexCallbackItem("first"),
                new ComplexCallbackItem { Text = "second", Parts = null },
                new ComplexCallbackItem { Text = "third", Parts = [] },
            ];

            await context.SaveChangesAsync(cancellationToken);

            return database;
        }
        catch
        {
            await database.DisposeAsync();

            throw;
        }
    }

    /// <summary>Uses bounded detached inputs and the existing single, subtree and two-tree import contracts.</summary>
    private static NavigationNode[] CreateComplexCompositionInputs(string shape)
        => Enumerable.Range(101, shape == "coordinated" ? 0 : shape == "single" ? 1 : shape == "subtree" ? 2 : 4)
            .Select(id => new NavigationNode { Id = id, Name = "Input " + id.ToString(CultureInfo.InvariantCulture) })
            .ToArray();

    /// <summary>Separates an actual coordinated reorder from the managed insertion entry points.</summary>
    private static async Task PrepareComplexCompositionOperationAsync(
        ComplexCallbackContext context,
        string shape,
        CancellationToken cancellationToken
    )
    {
        if (shape == "coordinated")
        {
            await PrepareComplexCallbackOrderingAsync(context, cancellationToken: cancellationToken);
        }
    }

    /// <summary>Runs the actual public operation rather than testing only an isolated tracker journal.</summary>
    private static Task RunComplexCompositionOperationAsync(
        ComplexCallbackContext context,
        string shape,
        NavigationNode[] inputs,
        CancellationToken cancellationToken
    )
        => shape == "coordinated"
            ? context.SaveChangesAsync(cancellationToken)
            : InsertComplexCallbackInputsAsync(context, shape, inputs, cancellationToken: cancellationToken);

    /// <summary>Combines mutations whose individual recovery can succeed while parent-child rebinding fails.</summary>
    private static void ChangeComplexComposition(ComplexCallbackRow row, string mutation)
    {
        switch (mutation)
        {
            case "remove-two":
                row.Items!.RemoveAt(0);
                row.Items.RemoveAt(0);
                break;
            case "nested-add-reverse":
                row.Items![0].Parts!.Add(new ComplexCallbackLeaf { Text = "callback part" });
                row.Items.Reverse();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    /// <summary>Creates pending Added or Deleted native state at the outer or nested collection boundary.</summary>
    private static void AddPendingComplexComposition(ComplexCallbackRow row, string pending)
    {
        if (pending == "deleted")
        {
            row.Items!.RemoveAt(0);
        }
        else if (pending == "nested")
        {
            row.Items![0].Parts!.Add(new ComplexCallbackLeaf { Text = "pending part" });
        }
        else
        {
            row.Items!.Add(new ComplexCallbackItem
            {
                Text = "pending item",
                Parts = [new ComplexCallbackLeaf { Text = "pending part" }],
            });
        }
    }

    /// <summary>Expands or clears an Added element before recovery must restore its shorter captured lanes.</summary>
    private static void ChangePendingComplexComposition(ComplexCallbackRow row, string pending)
    {
        if (pending == "deleted")
        {
            // WHY: A callback addition forces rebinding of the pending Deleted element and its native children.
            row.Items!.Add(NewComplexCallbackItem("callback item"));

            return;
        }

        var parts = pending == "nested" ? row.Items![0].Parts! : row.Items![^1].Parts!;
        parts.Add(new ComplexCallbackLeaf { Text = "callback part" });

        if (pending == "outer-clear")
        {
            parts.Clear();
        }
        else if (pending == "nested")
        {
            row.Items!.Reverse();
        }
    }

    /// <summary>Ignores managed facade inputs while retaining exact ordinary entity and scalar tracker state.</summary>
    private static PlannedEntryState[] CaptureComplexCompositionEntries(
        DbContext context,
        ComplexCallbackGraph graph
    )
        => CapturePlannedParentState(context).Where(entry => ReferenceEquals(entry.Entity, graph.Row.Entity)
            || ReferenceEquals(entry.Entity, graph.Sentinel.Entity)).ToArray();

    /// <summary>Copies intended ordinary payload independently of JSON serialization and database readback.</summary>
    private static ComplexCallbackStoredRow CaptureComplexCompositionStored(ComplexCallbackRow row)
        => new(row.Label, row.Version, row.Address?.Text,
            row.Items is null ? null : string.Join("|", row.Items.Select(item => item.Text + ":"
                + (item.Parts is null ? "null" : string.Join(",", item.Parts.Select(part => part.Text))))),
            row.Address?.Tags is null ? null : string.Join("|", row.Address.Tags.Select(tag => tag.Text)));

    /// <summary>Verifies retry persists the reorder and never resurrects rolled-back facade inputs.</summary>
    private static void AssertComplexCompositionHierarchyAfterRetry(
        string shape,
        PreparedOwnerStoreState before,
        PreparedOwnerStoreState after
    )
    {
        if (shape == "coordinated")
        {
            Assert.Equal("Zulu", after.Nodes.Single(node => node.Id == 2).Name);
            Assert.Equal(before.Nodes.Length, after.Nodes.Length);
        }
        else
        {
            Assert.Equal(before.Nodes, after.Nodes);
        }

        Assert.Equal("sentinel", after.Note);
        Assert.Equal(0, after.Issues);
    }
}
