namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies change tracking, key equality, and configured property access on every supported engine.</summary>
public abstract partial class ContractTests : ProviderTest
{
    private readonly ContractFixture _fixture;

    /// <summary>Creates a case using fixture-owned databases and isolated contract tables.</summary>
    /// <param name="fixture">The shared database owner.</param>
    protected ContractTests(
        IProviderFixture<ContractFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    private static readonly (long Left, long Right, int Depth, long Position)[] s_threeLevelChain =
    [
        (1, 6, 0, 0),
        (2, 5, 1, 0),
        (3, 4, 2, 0),
    ];

    private static readonly (long Left, long Right, int Depth, long Position)[] s_rootAndTwoChildren =
    [
        (1, 6, 0, 0),
        (2, 3, 1, 0),
        (4, 5, 1, 1),
    ];

    private static readonly (long Left, long Right, int Depth, long Position)[] s_parentAndChild =
    [
        (1, 4, 0, 0),
        (2, 3, 1, 0),
    ];

    private static readonly (long Left, long Right, int Depth, long Position)[] s_twoRoots =
    [
        (1, 2, 0, 0),
        (1, 2, 0, 0),
    ];

    /// <summary>Arranges a three-level hierarchy with independently allocated binary identity values.</summary>
    /// <param name="context">The empty context whose connection and schema will be initialized.</param>
    /// <returns>A task that completes when the fixture is persisted.</returns>
    private static async Task SeedBinaryHierarchyAsync(
        TreeContext context
    )
    {
        var tree = context
            .NestedSet<BinaryNode>()
            .ForScope(1);

        // Separate array instances expose reference-equality mistakes in parent resolution.
        await tree.InsertRootAsync(new BinaryNode { Id = [1, 2] }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync<byte[]>(
            new BinaryNode { Id = [3, 4] },
            [1, 2],
            cancellationToken: CancellationToken.None);

        await tree.InsertChildAsync<byte[]>(
            new BinaryNode { Id = [5, 6] },
            [3, 4],
            cancellationToken: CancellationToken.None);
    }

    /// <summary>Arranges a root and child using the configured field-access mapping.</summary>
    /// <param name="context">The empty context whose connection and schema will be initialized.</param>
    /// <returns>A task that completes when the fixture is persisted.</returns>
    private static async Task SeedFieldHierarchyAsync(
        FieldContext context
    )
    {
        var tree = context
            .NestedSet<FieldNode>()
            .ForScope(1);

        await tree.InsertRootAsync(new FieldNode { Id = 1 }, Guid.Empty, CancellationToken.None);
        await tree.InsertChildAsync(new FieldNode { Id = 2 }, 1, cancellationToken: CancellationToken.None);
    }
}
