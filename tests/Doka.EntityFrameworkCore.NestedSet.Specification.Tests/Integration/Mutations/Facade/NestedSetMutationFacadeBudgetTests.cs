namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects the target mutation facade from unbounded database round trips.</summary>
public abstract class NestedSetMutationFacadeBudgetTests : ProviderTest
{
    private const int InsertCommandBudget = 12;
    private const int InsertUpdateBudget = 5;
    private readonly RelationalFixture _fixture;

    /// <summary>Creates a command-budget test over the established provider fixture.</summary>
    protected NestedSetMutationFacadeBudgetTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Child insertion stays within a fixed command and set-update budget on every provider.</summary>
    [Fact]
    public async Task InsertChildHasBoundedCommandCount()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using (var setup = database.CreateContext())
        {
            await setup
                .NestedSet<TreeNode>()
                .ForScope(7)
                .InsertRootAsync(
                    new TreeNode { NodeId = 1 },
                    Guid.Parse("10101010-1010-1010-1010-101010101010"),
                    CancellationToken.None);
        }

        var probe = new CommandProbe();
        await using var context = database.CreateContext(probe);

        // Act
        await context
            .NestedSet<TreeNode>()
            .ForScope(7)
            .InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);

        var commandCount = probe.CommandCount;
        var updateCount = probe.UpdateCount;

        // Assert
        Assert.InRange(commandCount, 1, InsertCommandBudget);
        Assert.InRange(updateCount, 1, InsertUpdateBudget);
    }
}
