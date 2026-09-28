namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks that failure injection observes assigned bounds rather than SQL keyword substrings.</summary>
public abstract class CommandProbeTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Creates isolated cases using the shared relational engines.</summary>
    /// <param name="fixture">The fixture that owns database lifetime and resets.</param>
    protected CommandProbeTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A position-only CASE assignment must not trigger a bounds-write failure.</summary>
    [Fact]
    public async Task PositionCaseAssignmentDoesNotCountAsBoundsWrite()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        var tree = setup
            .NestedSet<TreeNode>()
            .ForScope(1);

        await tree.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        var probe = new CommandProbe();
        probe.Reset(failAfterBoundsUpdate: true);
        await using var context = database.CreateContext(probe);

        // Act
        var error = await Record.ExceptionAsync(() => context
            .Set<TreeNode>()
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.Position, node => node.Start == 1 ? 1 : 2),
                CancellationToken.None));

        // Assert
        Assert.Null(error);
        Assert.Equal(0, probe.BoundsUpdatesCompleted);
        Assert.Equal(0, probe.BoundsRowsAffected);
        Assert.Equal(
            1,
            await setup
                .Set<TreeNode>()
                .Select(node => node.Position)
                .SingleAsync(CancellationToken.None));
    }
}
