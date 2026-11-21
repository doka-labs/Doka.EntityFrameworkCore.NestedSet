using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyGraphRecoveryTests
{
    /// <summary>Preserves the complete native journal, scalar snapshot and EF property wrappers.</summary>
    /// <param name="rows">The number of complex rows, each containing two nested reference leaves.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(2_048)]
    [InlineData(20_000)]
    public async Task Construct_AlignedNativeCapture_PreservesGraphAndNativeState(
        int rows
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var payload = CreatePayload(rows);
        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        MarkPending(entry);
        var expectedScalars = ObserveScalars(entry);
        var expectedStates = ObserveStates(entry);
        var owner = NestedSetInsertionContract.Instance.Entry(entry);

        // Act
        var snapshot = new NestedSetComplexSnapshot(entry, owner);

        // Assert
        snapshot.Verify();
        Assert.Equal(expectedScalars, ObserveScalars(entry));
        Assert.Equal(expectedStates, ObserveStates(entry));
        Assert.Same(owner, NestedSetInsertionContract.Instance.Entry(entry));
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);

    }
}
