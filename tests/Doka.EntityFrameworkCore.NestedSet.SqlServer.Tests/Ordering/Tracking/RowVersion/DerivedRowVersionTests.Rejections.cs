namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

public sealed partial class DerivedRowVersionTests
{
    /// <summary>Explicit movement rejects tracked subtypes and retains their tokens and structure.</summary>
    [Fact]
    public async Task MoveToAsync_TrackedExplicitMove_RejectsDerivedNodesWithoutChangingTokens()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            interceptor: null);
        var tracked = await SeedAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var tokens = tracked
            .OfType<DerivedVersionTokenNode>()
            .ToDictionary(node => node.Id, node => node.Version.ToArray());

        var before = await SnapshotAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var hierarchy = context
            .NestedSet<DerivedVersionBaseNode>()
            .ForScope(1);

        // Act
        var rejection = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.MoveToAsync(2, 5, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal(before, await SnapshotAsync(context, cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(
            tracked,
            node => Assert.Equal(
                EntityState.Unchanged,
                context.Entry(node)
                    .State));

        Assert.All(
            tracked.OfType<DerivedVersionTokenNode>(),
            node =>
            {
                Assert.Equal(
                    tokens[node.Id],
                    context
                        .Entry(node)
                        .Property(value => value.Version)
                        .CurrentValue);

                Assert.Equal(
                    tokens[node.Id],
                    context
                        .Entry(node)
                        .Property(value => value.Version)
                        .OriginalValue);
            });

        await AssertValidAsync(context, cancellationToken: TestContext.Current.CancellationToken);
    }
}
