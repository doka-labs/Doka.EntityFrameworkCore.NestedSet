namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingKeyTests
{
    /// <summary>Stores the expected preorder shared by converted-key import cases.</summary>
    private static readonly int[] s_convertedPreorder = [1, 6, 21, 20, 2];

    /// <summary>Gets converted integer import cases under both application collection-translation overrides.</summary>
    public static TheoryData<ParameterTranslationMode> ConvertedBulkCollectionCases
    {
        get
        {
            var cases = new TheoryData<ParameterTranslationMode>
            {
                ParameterTranslationMode.Constant,
                ParameterTranslationMode.Parameter,
            };

            return cases;
        }
    }

    /// <summary>Converted native CLR keys use scalar bindings and preserve sorted import geometry.</summary>
    /// <param name="mode">The application's collection-translation preference.</param>
    [Theory]
    [MemberData(nameof(ConvertedBulkCollectionCases))]
    public async Task ConvertedSortedBulkRefreshUsesScalarBindingsAndPreservesOtherTrees(
        ParameterTranslationMode mode
    )
    {
        // Arrange
        await using var arranged = await _fixture.ResetAsync(Engine);
        var treeId = await ArrangeConvertedKeysAsync(arranged);
        var otherBefore = await ConvertedSnapshotAsync(
            arranged
                .Set<OrderingKeyNode<int, int?>>()
                .AsNoTracking()
                .Where(node => node.TreeId != treeId));

        var probe = new EnterpriseProbe();
        var providerOptions = ModelCompatibilityDatabase.Options<OrderingKeyContext>(
            Engine,
            arranged.Database.GetConnectionString()!,
            mode);

        var options = new DbContextOptionsBuilder<OrderingKeyContext>(providerOptions).AddInterceptors(probe).Options;

        await using var context = new OrderingKeyContext(options);
        var root = new OrderingKeyNode<int, int?>(6, "Alpha");
        var later = new OrderingKeyNode<int, int?>(20, "Zulu");
        var earlier = new OrderingKeyNode<int, int?>(21, "Alpha");
        OrderingKeyNode<int, int?>[] imported = [root, later, earlier];
        var branch = new NestedSetBranch<OrderingKeyNode<int, int?>>(
            root,
            [
                new NestedSetBranch<OrderingKeyNode<int, int?>>(later),
                new NestedSetBranch<OrderingKeyNode<int, int?>>(earlier),
            ]);

        var tree = context
            .NestedSet<OrderingKeyNode<int, int?>>()
            .ForScope(1);

        // Act
        await tree.InsertSubtreeAsync(branch, 1, CancellationToken.None);

        // Assert
        var persisted = await ConvertedSnapshotAsync(tree.InTree(treeId).Nodes);

        Assert.Equal(
            s_convertedPreorder,
            persisted
                .OrderBy(node => node.Left)
                .Select(node => node.Id));
        Assert.Equal((2L, 7L, 1, 0L), (root.Left, root.Right, root.Depth, root.Position));
        Assert.Equal((3L, 4L, 2, 0L), (earlier.Left, earlier.Right, earlier.Depth, earlier.Position));
        Assert.Equal((5L, 6L, 2, 1L), (later.Left, later.Right, later.Depth, later.Position));
        Assert.All(
            imported,
            node =>
            {
                Assert.Equal(ConvertedSnapshot(node), Assert.Single(persisted, row => row.Id == node.Id));
                Assert.Equal(EntityState.Detached, context.Entry(node).State);
            });

        Assert.Equal(
            otherBefore,
            await ConvertedSnapshotAsync(
                context
                    .Set<OrderingKeyNode<int, int?>>()
                    .AsNoTracking()
                    .Where(node => node.TreeId != treeId)));

        var report = await tree
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        Assert.True(report.IsValid);
        AssertConvertedRefreshBindings(probe, imported);
    }

    /// <summary>A late converted-key edit restores every input and all persisted trees after sorted refresh.</summary>
    /// <param name="mode">The application's collection-translation preference.</param>
    [Theory]
    [MemberData(nameof(ConvertedBulkCollectionCases))]
    public async Task ConvertedSortedBulkLateIdentityEditRestoresInputsAndAllTrees(
        ParameterTranslationMode mode
    )
    {
        // Arrange
        await using var arranged = await _fixture.ResetAsync(Engine);
        await ArrangeConvertedKeysAsync(arranged);
        var before = await ConvertedSnapshotAsync(
            arranged
                .Set<OrderingKeyNode<int, int?>>()
                .AsNoTracking());

        var table = arranged.Model.FindEntityType(typeof(OrderingKeyNode<int, int?>))!.GetTableName()!;
        var refresh = new BulkIntervalRefreshProbe(table);
        var commands = new EnterpriseProbe();
        var providerOptions = ModelCompatibilityDatabase.Options<OrderingKeyContext>(
            Engine,
            arranged.Database.GetConnectionString()!,
            mode);

        var options = new DbContextOptionsBuilder<OrderingKeyContext>(providerOptions)
            .AddInterceptors(refresh, commands)
            .Options;

        await using var context = new OrderingKeyContext(options);
        var root = new OrderingKeyNode<int, int?>(6, "Alpha")
        {
            Scope = 17,
            TreeId = Guid.NewGuid(),
            Left = 71,
            Right = 72,
            Depth = 4,
            Position = 73,
        };

        var later = new OrderingKeyNode<int, int?>(20, "Zulu");
        var earlier = new OrderingKeyNode<int, int?>(21, "Alpha");
        OrderingKeyNode<int, int?>[] imported = [root, later, earlier];
        var original = imported
            .Select(ConvertedSnapshot)
            .ToArray();

        var branch = new NestedSetBranch<OrderingKeyNode<int, int?>>(
            root,
            [
                new NestedSetBranch<OrderingKeyNode<int, int?>>(later),
                new NestedSetBranch<OrderingKeyNode<int, int?>>(earlier),
            ]);

        context.SavedChanges += (_, _) => refresh.Inserted = true;

        // WHY: Editing only the caller's key after the import writes leaves database rows intact. The late
        // verification must reject this mismatch and restore the earlier gap, payload, ordering, and input state.
        refresh.BeforeRefresh = (_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            root.Id = 600;

            return Task.CompletedTask;
        };

        var tree = context
            .NestedSet<OrderingKeyNode<int, int?>>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync(branch, 1, CancellationToken.None));

        refresh.Inserted = false;

        // Assert
        Assert.IsType<DbUpdateConcurrencyException>(error);
        Assert.Single(refresh.Commands);
        Assert.Equal(
            before,
            await ConvertedSnapshotAsync(
                context
                    .Set<OrderingKeyNode<int, int?>>()
                    .AsNoTracking()));
        Assert.Equal(original, imported.Select(ConvertedSnapshot));
        Assert.Empty(context.ChangeTracker.Entries());
        AssertConvertedRefreshBindings(commands, imported);
    }

    /// <summary>
    /// Creates overlapping bounds in another tree and another scope using the actual converted model.
    /// </summary>
    /// <param name="context">The fixture-owned empty ordering context.</param>
    /// <returns>The tree that the import will modify.</returns>
    private static async Task<Guid> ArrangeConvertedKeysAsync(
        OrderingKeyContext context
    )
    {
        var treeId = Guid.NewGuid();
        var tree = context
            .NestedSet<OrderingKeyNode<int, int?>>()
            .ForScope(1);

        await tree.InsertRootAsync(new OrderingKeyNode<int, int?>(1, "Root"), treeId, CancellationToken.None);
        await tree.InsertChildAsync(new OrderingKeyNode<int, int?>(2, "Middle"), 1, CancellationToken.None);
        await tree.InsertRootAsync(
            new OrderingKeyNode<int, int?>(9, "Other tree"),
            Guid.NewGuid(),
            CancellationToken.None);

        await tree.InsertChildAsync(new OrderingKeyNode<int, int?>(8, "Other child"), 9, CancellationToken.None);
        var other = context
            .NestedSet<OrderingKeyNode<int, int?>>()
            .ForScope(2);

        await other.InsertRootAsync(
            new OrderingKeyNode<int, int?>(11, "Other scope"),
            Guid.NewGuid(),
            CancellationToken.None);

        await other.InsertChildAsync(new OrderingKeyNode<int, int?>(12, "Other child"), 11, CancellationToken.None);

        return treeId;
    }

    /// <summary>
    /// Checks the qualified converted-key fallback on the command that actually refreshes the import.
    /// </summary>
    /// <param name="probe">The command and scalar-value observer.</param>
    /// <param name="imported">The imported identities after successful completion or rollback.</param>
    private static void AssertConvertedRefreshBindings(
        EnterpriseProbe probe,
        IReadOnlyList<OrderingKeyNode<int, int?>> imported
    )
    {
        var refreshIndexes = probe
            .Commands
            .Select((
                command,
                index
            ) => (command, index))
            .Where(item => item.command.Contains(NestedSetDiagnostics.BulkRefreshTag, StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();

        var index = Assert.Single(refreshIndexes);
        var sql = probe.Commands[index];
        var parameters = probe.ParameterValues[index];

        // WHY: EF can support some converted collections, but the library's qualified native matrix excludes
        // converters. The actual refresh must use mapped scalar parameters under either application override.
        Assert.Contains(" OR ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" IN (", sql, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            imported,
            node => Assert.Contains(
                parameters,
                value => value is string text
                    && text == node.Id.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Reads every payload and structural field for exact tree-isolation and rollback comparisons.</summary>
    /// <param name="query">The independent persisted rows.</param>
    /// <returns>The complete state ordered by stable identity.</returns>
    private static async Task<ConvertedKeySnapshot[]> ConvertedSnapshotAsync(
        IQueryable<OrderingKeyNode<int, int?>> query
    )
    {
        var nodes = await query
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        return nodes
            .Select(ConvertedSnapshot)
            .ToArray();
    }

    /// <summary>Captures exact integer identity, nullable parent, payload, and geometry without erased keys.</summary>
    /// <param name="node">The imported or persisted entity.</param>
    /// <returns>The complete state used by independent assertions.</returns>
    private static ConvertedKeySnapshot ConvertedSnapshot(
        OrderingKeyNode<int, int?> node
    ) => new(
        node.Id,
        node.Scope,
        node.TreeId,
        node.ParentId,
        node.Name,
        node.Left,
        node.Right,
        node.Depth,
        node.Position);

    /// <summary>Retains complete state for converted-key geometry, isolation, and rollback checks.</summary>
    /// <param name="Id">The integer model identity stored as text.</param>
    /// <param name="Scope">The independent hierarchy scope.</param>
    /// <param name="TreeId">The independent coordinate identity.</param>
    /// <param name="Parent">The nullable integer parent stored as text.</param>
    /// <param name="Name">The sibling ordering value.</param>
    /// <param name="Left">The left boundary.</param>
    /// <param name="Right">The right boundary.</param>
    /// <param name="Depth">The depth from the root.</param>
    /// <param name="Position">The position among siblings.</param>
    private sealed record ConvertedKeySnapshot(
        int Id,
        int Scope,
        Guid TreeId,
        int? Parent,
        string Name,
        long Left,
        long Right,
        int Depth,
        long Position
    );
}
