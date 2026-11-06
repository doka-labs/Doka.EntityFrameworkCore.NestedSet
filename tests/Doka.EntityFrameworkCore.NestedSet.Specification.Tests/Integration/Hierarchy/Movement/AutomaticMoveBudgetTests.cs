namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Bounds locked placement reads while preserving automatic and explicit subtree destinations.</summary>
public abstract partial class AutomaticMoveBudgetTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Uses the existing independently owned ordering tables and real provider connections.</summary>
    protected AutomaticMoveBudgetTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Automatic moves resolve TreeId and native ordering without a count or discarded placement.</summary>
    [Theory]
    [InlineData("Strict", false)]
    [InlineData("Strict", true)]
    [InlineData("Flexible", false)]
    [InlineData("Flexible", true)]
    public async Task AutomaticPlacementReadsOnlyTheRequiredDestination(
        string mode,
        bool upperGroup
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            mode,
            probe);

        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        // Act
        await (upperGroup
            ? tree.MoveToAsync(3, 0, CancellationToken.None)
            : tree.MoveToAsync(3, 2, CancellationToken.None));

        // Assert
        // WHY: One public query resolves both TreeIds; bounds are read again after the exact-tree lock is held.
        Assert.Single(FacadeIdentityReads(probe, mode, context));
        var reads = LockedHierarchyReads(probe, mode, context);
        Assert.Equal(4, reads.Length);
        Assert.DoesNotContain(reads, sql => sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reads, sql => sql.Contains("MAX(", StringComparison.OrdinalIgnoreCase));
        Assert.Single(reads, sql => sql.Contains("LAG(", StringComparison.OrdinalIgnoreCase));
        await AssertMoveAsync(
            context,
            upperGroup,
            automatic: true,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Flexible placement resolves TreeId and retains its explicit destination instead of domain order.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlexibleManualPlacementKeepsItsOwnDestination(
        bool upperGroup
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            "Flexible");

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            "Flexible",
            probe);

        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        // Act
        await (upperGroup
            ? tree.MoveAfterAsync(3, 2, CancellationToken.None)
            : tree.MoveAfterAsync(3, 6, CancellationToken.None));

        // Assert
        Assert.Single(FacadeIdentityReads(probe, "Flexible", context));
        var reads = LockedHierarchyReads(probe, "Flexible", context);
        Assert.Equal(3, reads.Length);

        // WHY: Before/After reuse the sibling's position; unlike LastChild, neither requires a sibling count.
        Assert.DoesNotContain(reads, sql => sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reads, sql => sql.Contains("LAG(", StringComparison.OrdinalIgnoreCase));
        await AssertMoveAsync(
            context,
            upperGroup,
            automatic: false,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Strict ordering rejects explicit destinations before resolving or writing hierarchy coordinates.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictManualPlacementRemainsRejected(
        bool upperGroup
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var before = await SnapshotAsync(setup, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            "Strict",
            probe);

        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => upperGroup
            ? tree.MoveAfterAsync(3, 2, CancellationToken.None)
            : tree.MoveAfterAsync(3, 6, CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.ManualPlacementNotAllowed,
            Assert.IsType<NestedSetException>(error)
                .Code);

        Assert.Empty(FacadeIdentityReads(probe, "Strict", context));
        Assert.Empty(LockedHierarchyReads(probe, "Strict", context));
        Assert.DoesNotContain(probe.Commands, IsHierarchyWrite);
        Assert.Equal(before, await SnapshotAsync(setup, cancellationToken: TestContext.Current.CancellationToken));
    }
}
