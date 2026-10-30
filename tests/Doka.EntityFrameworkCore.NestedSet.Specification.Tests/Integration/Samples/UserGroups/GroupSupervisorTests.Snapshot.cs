using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Probes a real writer between chain hops rather than inferring isolation from the helper name.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Role, routing, and membership changes between readers cannot produce a hybrid chain (U54).</summary>
    [Fact]
    public async Task EveryHopReadsTheSameSnapshotAcrossConcurrentRoutingAndRoleChanges()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await EnableSnapshotAsync(context);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerProbe = new SupervisorWriterProbe(entered);
        var options = ModelCompatibilityDatabase.Options<App.UserGroupContext>(
            Engine, context.Database.GetConnectionString()!);

        await using var writer = new App.UserGroupContext(new DbContextOptionsBuilder<App.UserGroupContext>(options)
            .AddInterceptors(writerProbe).Options);

        Task? mutation = null;
        var writerBlocked = false;
        probe.BeforeReadAsync = async (number, token) =>
        {
            if (number != 2)
            {
                return;
            }

            // WHY: SQLite's async driver may wait synchronously for its writer lock; isolate that wait from the
            // reader continuation.
            mutation = Task.Run(async () =>
            {
                await writer.UserMetadatas.Where(value => value.TenantId == data.TenantId
                        && value.UserId == data.Users[1] && value.FieldCode == "Route")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, "lane-blue"), token);
                await writer.MembershipRoles
                    .Where(role => role.TenantId == data.TenantId && role.UserId == data.Users[4])
                    .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.Visible, false), token);
                await writer.UserGroupMemberships.Where(member => member.TenantId == data.TenantId
                        && member.UserId == data.Users[4] && member.UserGroupId == data.Groups[1])
                    .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.Visible, false), token);
                await writer.NestedSet<App.UserGroup>().ForScope(data.TenantId)
                    .MoveToAsync(data.Groups[4], data.Groups[6], token);
            }, CancellationToken.None);
            await entered.Task.WaitAsync(token);
            if (Engine == "Sqlite")
            {
                writerBlocked = !mutation.IsCompleted;
            }
            else
            {
                await mutation.WaitAsync(token);
            }
        };

        var request = ChainRequest(data, true);
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.FollowOriginalPathAsync(
            context, data.TenantId, request, CancellationToken.None);

        await mutation!;

        // Assert
        var branch = Assert.Single(result.Branches);
        Assert.Equal(new[] { data.Users[1], data.Users[4] }, branch.Hops.Select(hop => hop.Evidence.CandidateUserId));
        Assert.Equal(App.SupervisorChainStatus.ReachedRequestedHop, branch.Status);
        Assert.Equal(2, probe.Commands.Count);
        Assert.True(Engine != "Sqlite" || writerBlocked);
        Assert.True(mutation!.IsCompletedSuccessfully);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>
    /// Cancellation before or at the reader remains cancellation rather than a missing supervisor (U57).
    /// </summary>
    /// <param name="duringRead">Whether cancellation begins at the reader instead of before execution.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupervisorReaderPropagatesCancellation(bool duringRead)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        if (duringRead)
        {
            probe.BeforeReadAsync = async (_, _) =>
            {
                await cancellation.CancelAsync();
            };
        }
        else
        {
            await cancellation.CancelAsync();
        }

        var request = data.Request(group: data.Groups[5]);
        probe.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => App.SupervisorQueries.ResolveAsync(context, data.TenantId,
            request, App.SupervisorSelection.RequireUnique, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.InRange(probe.Commands.Count, 0, duringRead ? 1 : 0);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Signals the writer's first real payload command before its provider can block on a reader lock.
    /// </summary>
    /// <param name="entered">The signal completed when the writer reaches its first payload command.</param>
    private sealed class SupervisorWriterProbe(TaskCompletionSource entered) : DbCommandInterceptor
    {
        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            entered.TrySetResult();

            return ValueTask.FromResult(result);
        }
    }
}
