namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportValidation
{
    /// <summary>Consumes and disposes the complete catalog before applying catalog rejection precedence.</summary>
    internal static async Task RequireCatalogAsync(
        ValidatedSignalReport request,
        IAsyncEnumerable<SignalCatalogRow> rows,
        CancellationToken cancellationToken
    )
    {
        var catalog = new CatalogAccumulator();
        await foreach (var row in rows
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            catalog.Add(row);
        }

        catalog.Complete(request);
    }

    /// <summary>Consumes and disposes every aggregate before rejecting duplicates or allocating final cells.</summary>
    internal static async Task<SignalReportCell[]> CreateGridAsync(
        ValidatedSignalReport request,
        IAsyncEnumerable<SignalReportTotal> rows,
        CancellationToken cancellationToken
    )
    {
        var totals = new GridAccumulator(request, 0);
        await foreach (var row in rows
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            totals.Add(row);
        }

        return totals.Complete();
    }
}
