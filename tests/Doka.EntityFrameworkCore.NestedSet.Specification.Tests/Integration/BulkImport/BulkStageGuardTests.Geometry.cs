namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects live staged geometry after payload callbacks without rejecting equivalent assignments.</summary>
public abstract partial class BulkStageGuardTests
{
    /// <summary>Combines every supported engine with each scalar geometry role owned by the import.</summary>
    public static TheoryData<string> SavedGeometryCases
    {
        get
        {
            var cases = new TheoryData<string>();
            var roles = new[]
            {
                nameof(BulkStageTextNode.Left),
                nameof(BulkStageTextNode.Right),
                nameof(BulkStageTextNode.Depth),
                nameof(BulkStageTextNode.Position),
            };

            foreach (var role in roles)
            {
                cases.Add(role);
            }

            return cases;
        }
    }

    /// <summary>A post-payload geometry change rejects the import and restores every input and existing tree.</summary>
    /// <param name="role">The managed geometry role changed after its payload row has been persisted.</param>
    [Theory]
    [MemberData(nameof(SavedGeometryCases))]
    public async Task SavedCallbackGeometryChangeRestoresInputsAndEveryPersistedTree(
        string role
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        var otherTreeId = Guid.NewGuid();
        await SeedGeometryTreesAsync(context, treeId, otherTreeId);
        var persistedBefore = await ReadGeometryStatesAsync(context);
        var inputs = GeometryInputs();
        var inputBefore = inputs
            .Select(CaptureGeometryState)
            .ToArray();

        var branch = new NestedSetBranch<BulkStageTextNode>(
            inputs[0],
            [new NestedSetBranch<BulkStageTextNode>(inputs[1])]);

        var callbacks = 0;

        // WHY: SavedChanges runs after the payload write. A later refresh must not conceal a callback's live
        // structural edit, and rollback must undo the opened gap as well as both imported payload rows.
        context.SavedChanges += (_, _) =>
        {
            callbacks++;
            AssignGeometryRole(inputs[0], role, CaptureGeometryState(inputs[0]), 1);
        };

        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope("S");

        // Act
        var failure = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync(branch, "A", CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(failure)
                .Code);
        Assert.Equal(1, callbacks);
        Assert.Equal(inputBefore, inputs.Select(CaptureGeometryState));
        Assert.Equal(persistedBefore, await ReadGeometryStatesAsync(context));
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertGeometryTreesValidAsync(context, treeId, otherTreeId);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>An equivalent saved assignment preserves the imported branch's full persisted structure.</summary>
    /// <param name="role">The geometry role copied from its current saved value.</param>
    [Theory]
    [MemberData(nameof(SavedGeometryCases))]
    public async Task EquivalentSavedCallbackGeometryKeepsExactImportedStructure(
        string role
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        var otherTreeId = Guid.NewGuid();
        await SeedGeometryTreesAsync(context, treeId, otherTreeId);
        var persistedBefore = await ReadGeometryStatesAsync(context);
        var inputs = GeometryInputs();
        var branch = new NestedSetBranch<BulkStageTextNode>(
            inputs[0],
            [new NestedSetBranch<BulkStageTextNode>(inputs[1])]);

        var callbacks = 0;

        // WHY: Application callbacks may copy unchanged saved values. The import must inspect their resulting
        // geometry without treating the mere assignment as an unauthorized structural change.
        context.SavedChanges += (_, _) =>
        {
            callbacks++;
            AssignGeometryRole(inputs[0], role, CaptureGeometryState(inputs[0]), 0);
        };

        var expectedInputs = new[]
        {
            new GeometryState(
                "C",
                "S",
                treeId,
                "A",
                4,
                7,
                1,
                1),
            new GeometryState(
                "D",
                "S",
                treeId,
                "C",
                5,
                6,
                2,
                0),
        };

        var expectedPersisted = persistedBefore
            .Select(node => node.Id == "A" ? node with { Right = 8 } : node)
            .Concat(expectedInputs)
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope("S");

        // Act
        await tree.InsertSubtreeAsync(branch, "A", CancellationToken.None);

        // Assert
        Assert.Equal(1, callbacks);
        Assert.Equal(expectedInputs, inputs.Select(CaptureGeometryState));
        Assert.Equal(expectedPersisted, await ReadGeometryStatesAsync(context));
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertGeometryTreesValidAsync(context, treeId, otherTreeId);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Seeds another tree and Scope alongside the destination through public import contracts.</summary>
    private static async Task SeedGeometryTreesAsync(
        BulkStageGuardContext context,
        Guid treeId,
        Guid otherTreeId
    )
    {
        await context
            .NestedSet<BulkStageTextNode>()
            .ForScope("S")
            .InsertForestAsync(
                [
                    new NestedSetTreeImport<BulkStageTextNode, Guid>(
                        treeId,
                        new NestedSetBranch<BulkStageTextNode>(
                            new BulkStageTextNode { Id = "A" },
                            [new NestedSetBranch<BulkStageTextNode>(new BulkStageTextNode { Id = "B" })])),
                    new NestedSetTreeImport<BulkStageTextNode, Guid>(
                        otherTreeId,
                        new NestedSetBranch<BulkStageTextNode>(
                            new BulkStageTextNode { Id = "E" },
                            [new NestedSetBranch<BulkStageTextNode>(new BulkStageTextNode { Id = "F" })])),
                ],
                CancellationToken.None);

        await context
            .NestedSet<BulkStageTextNode>()
            .ForScope("Other")
            .InsertForestAsync(
                [
                    new NestedSetTreeImport<BulkStageTextNode, Guid>(
                        treeId,
                        new NestedSetBranch<BulkStageTextNode>(
                            new BulkStageTextNode { Id = "G" },
                            [new NestedSetBranch<BulkStageTextNode>(new BulkStageTextNode { Id = "H" })])),
                ],
                CancellationToken.None);
    }

    /// <summary>Creates caller-owned inputs whose original managed values must all survive a rejected import.</summary>
    private static BulkStageTextNode[] GeometryInputs() =>
    [
        new()
        {
            Id = "C",
            Scope = "original-root",
            TreeId = Guid.NewGuid(),
            ParentId = "original-root-parent",
            Left = 71,
            Right = 74,
            Depth = 5,
            Position = 11,
        },
        new()
        {
            Id = "D",
            Scope = "original-child",
            TreeId = Guid.NewGuid(),
            ParentId = "original-child-parent",
            Left = 81,
            Right = 82,
            Depth = 6,
            Position = 12,
        },
    ];

    /// <summary>Copies or changes one saved geometry role while retaining the remaining callback values.</summary>
    private static void AssignGeometryRole(
        BulkStageTextNode node,
        string role,
        GeometryState saved,
        int delta
    )
    {
        switch (role)
        {
            case nameof(BulkStageTextNode.Left):
                node.Left = saved.Left + delta;
                break;
            case nameof(BulkStageTextNode.Right):
                node.Right = saved.Right + delta;
                break;
            case nameof(BulkStageTextNode.Depth):
                node.Depth = saved.Depth + delta;
                break;
            case nameof(BulkStageTextNode.Position):
                node.Position = saved.Position + delta;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown managed geometry role.");
        }
    }

    /// <summary>Reads every persisted row without permitting tracker state to conceal rollback discrepancies.</summary>
    private static async Task<GeometryState[]> ReadGeometryStatesAsync(
        BulkStageGuardContext context
    )
    {
        var nodes = await context
            .Set<BulkStageTextNode>()
            .AsNoTracking()
            .ToArrayAsync(CancellationToken.None);

        return nodes
            .Select(CaptureGeometryState)
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Checks the destination, its neighboring tree, and the same TreeId in an independent Scope.</summary>
    private static async Task AssertGeometryTreesValidAsync(
        BulkStageGuardContext context,
        Guid treeId,
        Guid otherTreeId
    )
    {
        Assert.Empty(
            (await context
                .NestedSet<BulkStageTextNode>()
                .ForScope("S")
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);

        Assert.Empty(
            (await context
                .NestedSet<BulkStageTextNode>()
                .ForScope("S")
                .InTree(otherTreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);

        Assert.Empty(
            (await context
                .NestedSet<BulkStageTextNode>()
                .ForScope("Other")
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Snapshots every input and persisted field independently from application value comparers.</summary>
    private static GeometryState CaptureGeometryState(
        BulkStageTextNode node
    ) => new(
        node.Id,
        node.Scope,
        node.TreeId,
        node.ParentId,
        node.Left,
        node.Right,
        node.Depth,
        node.Position);

    /// <summary>Retains exact immutable managed values for rollback and successful persistence assertions.</summary>
    private readonly record struct GeometryState(
        string Id,
        string Scope,
        Guid TreeId,
        string? ParentId,
        long Left,
        long Right,
        int Depth,
        long Position
    );
}
