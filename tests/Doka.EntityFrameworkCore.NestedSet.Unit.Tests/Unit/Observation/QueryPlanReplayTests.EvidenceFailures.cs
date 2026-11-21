namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class QueryPlanReplayTests
{
    /// <summary>Diagnostic output failure cannot replace the failure that caused evidence collection.</summary>
    /// <param name="canceled">Whether the original operation failed through cancellation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainFailureAsync_OutputCannotBeWritten_PreservesOriginalFailure(
        bool canceled
    )
    {
        // Arrange
        await using var connection = new PlanReplayConnection(() => Results(Plan("unused")));
        await using var services = CreateServices(connection);
        await using var context = new DbContext(services.Options);
        Exception original = canceled
            ? new OperationCanceledException("Original canceled read.")
            : new InvalidOperationException("Original failed read.");

        var probe = Probe();

        // Act
        var actual = await Record.ExceptionAsync(async () =>
        {
            try
            {
                throw original;
            }
            catch (Exception failure)
            {
                // WHY: A null path character deterministically rejects output without altering process-wide paths.
                await CompositionCapacityProfiles.RetainFailureAsync(
                    context,
                    "Sqlite",
                    "unwritable\0profile",
                    probe,
                    failure);

                throw;
            }
        });

        // Assert
        Assert.Same(original, actual);
        Assert.IsAssignableFrom<Exception>(original.Data["NestedSet.CapacityEvidenceFailure"]);
    }
}
