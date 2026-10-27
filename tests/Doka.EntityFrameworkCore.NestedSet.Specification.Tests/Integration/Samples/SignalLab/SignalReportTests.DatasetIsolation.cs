using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Repeated scenario seeds own every isolation scope and remain readable beside another explicit tenant.</summary>
    [Fact]
    public async Task RepeatedScenarioSeedsKeepTheirIsolationTenantsIndependent()
    {
        // Arrange
        // WHY: An explicitly owned database makes the four-scope/six-tree oracle independent of
        // earlier scenarios and concurrently executing suites, without disposing the shared fixture.
        await using var owned = new ModelCompatibilityDatabase();
        var probe = new SignalReportProbe();
        await using var context = await owned.CreateContextAsync<SignalLabContext>(Engine,
            static options => new SignalLabContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, probe.Materialized);

        if (Engine == "SqlServer")
        {
            await context.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON",
                CancellationToken.None);
        }

        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();

        // Act
        var first = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, firstTenant);
        var second = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, secondTenant);
        var firstReport = await SignalReportBuilder.ReadAsync(context, SingleAnchor(first), CancellationToken.None);
        var secondReport = await SignalReportBuilder.ReadAsync(context, SingleAnchor(second), CancellationToken.None);
        var tenants = await context.Nodes.Select(node => node.TenantId).Distinct()
            .ToArrayAsync(CancellationToken.None);

        var treeCount = await context.Nodes.Select(node => new
        {
            node.TenantId,
            TreeId = EF.Property<Guid>(node, SignalStorage.TreeId),
        }).Distinct().CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(70, Assert.Single(firstReport).Value);
        Assert.Equal(70, Assert.Single(secondReport).Value);
        Assert.Equal(4, tenants.Length);
        Assert.Contains(firstTenant, tenants);
        Assert.Contains(secondTenant, tenants);
        Assert.Equal(6, treeCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
