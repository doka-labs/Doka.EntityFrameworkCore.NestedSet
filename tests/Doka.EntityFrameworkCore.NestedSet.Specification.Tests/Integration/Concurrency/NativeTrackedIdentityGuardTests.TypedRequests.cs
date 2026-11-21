namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Validates all exact generic role types before a constructor can execute any database command.</summary>
    [Theory]
    [InlineData("key")]
    [InlineData("scope")]
    [InlineData("tree")]
    public async Task Construct_Executor_RejectsIncorrectGenericIdentityRoles(
        string role
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var entityType = context.Model.FindEntityType(typeof(BroadTreeIdNode))!;
        probe.Armed = true;

        // Act
        var failure = Record.Exception(() =>
        {
            switch (role)
            {
                case "key":
                    _ = new NestedSetMutationExecutor<BroadTreeIdNode, long, BroadTreeId, NestedSetNoScope>(
                        context,
                        entityType);

                    break;
                case "scope":
                    _ = new NestedSetMutationExecutor<BroadTreeIdNode, int, BroadTreeId, string>(context, entityType);
                    break;
                case "tree":
                    _ = new NestedSetMutationExecutor<BroadTreeIdNode, int, string, NestedSetNoScope>(
                        context,
                        entityType);

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        });

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, probe.Commands);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Rejects foreign model lock requests on both consistent reads and explicit writes before SQL.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteReadAsync_Executor_RejectsForeignHierarchyRequests(
        bool read
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        await using var foreign = await _fixture.CreateContextAsync<BroadGuardTreeContext>(
            Engine,
            static options => new BroadGuardTreeContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var executor = new NestedSetMutationExecutor<BroadTreeIdNode, int, BroadTreeId, NestedSetNoScope>(
            context,
            context.Model.FindEntityType(typeof(BroadTreeIdNode))!);

        var requests = new[]
        {
            new NestedSetTreeLockRequest<BroadTreeId, NestedSetNoScope>(
                foreign.Model.FindEntityType(typeof(BroadTreeIdNode))!,
                default,
                new BroadTreeId("foreign-request"),
                NestedSetTreeLockMode.Existing),
        };

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => read
            ? executor.ExecuteReadAsync(_ => Task.CompletedTask, requests, CancellationToken.None)
            : executor.ExecuteAsync(_ => Task.CompletedTask, requests, CancellationToken.None));

        // Assert
        Assert.Equal(
            "requests",
            Assert.IsType<ArgumentException>(failure)
                .ParamName);

        Assert.Equal(0, probe.Commands);
        Assert.Null(context.Database.CurrentTransaction);
    }
}
