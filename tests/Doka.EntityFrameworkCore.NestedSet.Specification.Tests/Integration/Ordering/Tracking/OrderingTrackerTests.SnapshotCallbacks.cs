namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTrackerTests
{
    /// <summary>Restores previously unchanged unrelated payload modified by a callback before a late failure.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrderingRestoresUnchangedCallbackPayload(
        bool acceptChanges
    )
    {
        // Arrange
        var database = await CreateDatabaseAsync(Engine, cancellationToken: TestContext.Current.CancellationToken);
        await using var setup = new TrackerContext(
            await OptionsAsync(
                database,
                cancellationToken: TestContext.Current.CancellationToken));

        var input = new TrackerAddition { Value = "unchanged application payload" };
        await setup.AddAsync(input, CancellationToken.None);
        await setup.SaveChangesAsync(CancellationToken.None);
        var probe = new TrackerFailureProbe();
        await using var context = new TrackerContext(
            await OptionsAsync(
                database,
                cancellationToken: TestContext.Current.CancellationToken,
                probe));

        var payload = await context
            .Set<TrackerAddition>()
            .SingleAsync(CancellationToken.None);

        var node = await LoadNodeAsync(context, 3, cancellationToken: TestContext.Current.CancellationToken);
        node.Name = "Zulu";
        context.ChangeTracker.DetectChanges();
        var before = Capture(context, payload);
        context.SavingChanges += (_, _) => payload.Value = "callback mutation";

        // Act
        var failure = await Record.ExceptionAsync(() =>
            context.SaveChangesAsync(acceptChanges, CancellationToken.None));

        // Assert
        Assert.IsType<TrackerInjectedException>(failure);
        Assert.Equal(EntityState.Unchanged, before.State);
        AssertSnapshot(before, Capture(context, payload));
        Assert.Equal("unchanged application payload", payload.Value);
        await using var verification = new TrackerContext(
            await OptionsAsync(
                database,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "unchanged application payload",
            await verification
                .Set<TrackerAddition>()
                .Select(value => value.Value)
                .SingleAsync(CancellationToken.None));
    }
}
