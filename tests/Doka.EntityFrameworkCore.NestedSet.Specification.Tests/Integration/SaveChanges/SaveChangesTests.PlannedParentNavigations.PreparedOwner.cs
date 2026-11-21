using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Known facade inputs retained by a principal collection do not block later ordinary saves.</summary>
    [Theory]
    [InlineData("single", false)]
    [InlineData("single", true)]
    [InlineData("subtree", false)]
    [InlineData("subtree", true)]
    [InlineData("forest", false)]
    [InlineData("forest", true)]
    public async Task SaveChangesAsync_PreparedOwnerInput_AllowsFollowingOrdinarySave(
        string shape,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPreparedNavigationDestinationAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var callback = new SaveBoundaryCallback();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback);

        var owner = await context
            .Set<NavigationScopeOwner>()
            .SingleAsync(value => value.Value == "owner", CancellationToken.None);

        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var noteValue = context
            .Entry(note)
            .Property(value => value.Value);

        var inputs = PreparedNavigationInputs(shape, owner.Id);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (!automaticDetection)
        {
            owner.Nodes.Add(inputs[0]);
        }

        IUpdateEntry? inputEntry = null;
        callback.OnSaving = current =>
        {
            if (automaticDetection && !owner.Nodes.Contains(inputs[0]))
            {
                // WHY: Automatic mode's existing preflight must not attach a new graph before authorization.
                owner.Nodes.Add(inputs[0]);
            }

            inputEntry = current
                .GetService<IUpdateAdapterFactory>()
                .Create()
                .Entries
                .Single(entry => ReferenceEquals(
                    entry.ToEntityEntry()
                        .Entity,
                    inputs[0]));
        };

        await InsertPreparedNavigationShapeAsync(
            context,
            shape,
            inputs,
            cancellationToken: TestContext.Current.CancellationToken);

        var stateAfterInsertion = inputEntry?.EntityState;
        callback.OnSaving = null;
        noteValue.CurrentValue = "following payload";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedNote = await verification
            .Set<NavigationNote>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var count = await verification
            .Set<NavigationNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(EntityState.Detached, stateAfterInsertion);
        Assert.Contains(inputs[0], owner.Nodes);
        Assert.Equal("following payload", storedNote);
        Assert.Equal(4 + inputs.Length, count);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Automatic detection still rejects a prepared input registered before facade authorization.</summary>
    [Theory]
    [InlineData("single")]
    [InlineData("subtree")]
    [InlineData("forest")]
    public async Task InsertPreparedNavigationShapeAsync_AutomaticPreparedOwnerInput_PreservesPendingWritePreflight(
        string shape
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPreparedNavigationDestinationAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var owner = await context
            .Set<NavigationScopeOwner>()
            .SingleAsync(value => value.Value == "owner", CancellationToken.None);

        var inputs = PreparedNavigationInputs(shape, owner.Id);
        owner.Nodes.Add(inputs[0]);

        // Act
        var failure = await Record.ExceptionAsync(() => InsertPreparedNavigationShapeAsync(
            context,
            shape,
            inputs,
            cancellationToken: TestContext.Current.CancellationToken));

        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var count = await verification
            .Set<NavigationNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Contains("no pending tracked changes", rejection.Message, StringComparison.Ordinal);
        Assert.Equal(0, probe.Writes);
        Assert.Equal(4, count);
    }

    /// <summary>The persisted-input exception never authorizes an unrelated untracked collection member.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_ArbitraryUntrackedOwnerMemberStill_RejectsFollowingSave(
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var owner = await context
            .Set<NavigationScopeOwner>()
            .SingleAsync(value => value.Value == "owner", CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        owner.Nodes.Add(
            new NavigationNode
            {
                Id = 999,
                Scope = owner.Id,
                Name = "Unrequested",
            });

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var count = await verification
            .Set<NavigationNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(0, probe.Writes);
        Assert.Equal(3, count);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
