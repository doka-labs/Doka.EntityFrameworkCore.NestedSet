namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks that configured ordering changes only saves that actually modify the hierarchy order.</summary>
public abstract partial class SaveChangesTests : ProviderTest
{
    private static readonly Guid s_treeId = Guid.Parse("ef98dd74-c2e7-4ca7-93fe-bf0fd45bc955");
    private readonly OrderingFixture _fixture;

    /// <summary>Creates a case using independently provisioned ordering tables.</summary>
    /// <param name="fixture">The real relational databases shared by this test class.</param>
    protected SaveChangesTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Creates one tree with two ordered children for callback and pooled-lease tests.</summary>
    /// <param name="context">The initialized context that owns the fixture tables.</param>
    private static async Task SeedAsync(
        DbContext context
    )
    {
        var hierarchy = context
            .NestedSet<OrderingNode>()
            .ForScope(1);
        await hierarchy.InsertRootAsync(
            new OrderingNode
            {
                Id = 10,
                Name = "Root",
            },
            s_treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Alpha",
            },
            10,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new OrderingNode
            {
                Id = 2,
                Name = "Bravo",
            },
            10,
            CancellationToken.None);
    }
}
