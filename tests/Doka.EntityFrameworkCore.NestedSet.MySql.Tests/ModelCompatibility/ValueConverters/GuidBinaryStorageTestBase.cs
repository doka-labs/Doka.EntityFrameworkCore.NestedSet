namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Qualifies typed Guid and converted byte-array storage through native hierarchy operations.</summary>
public abstract class GuidBinaryStorageTestBase : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;
    private readonly ITestOutputHelper _output;

    /// <summary>Retains the collection's database and immutable provider identity.</summary>
    /// <param name="fixture">The existing provider-bound model-compatibility fixture.</param>
    /// <param name="output">The output retaining native transport evidence.</param>
    protected GuidBinaryStorageTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _fixture = fixture.Value;
        _output = output;
    }

    /// <summary>Typed materialization and tracked refresh retain every identity role and nullable parent.</summary>
    /// <param name="converted">Whether EF converts Guid identities to byte arrays before driver access.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertAsync_BinaryGuidIdentities_PreserveParentsAndScopeTreeIsolation(
        bool converted
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await CreateContextAsync(
            converted,
            new UnicodeIdentityCommandProbe(),
            cancellationToken);

        var scope = Guid.NewGuid();
        var otherScope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var otherTreeId = Guid.NewGuid();
        var root = new GuidBinaryStorageNode { Id = Guid.NewGuid() };
        var child = new GuidBinaryStorageNode { Id = Guid.NewGuid() };
        var otherTree = new GuidBinaryStorageNode { Id = Guid.NewGuid() };
        var otherPartition = new GuidBinaryStorageNode { Id = Guid.NewGuid() };
        var hierarchy = context.NestedSet<GuidBinaryStorageNode>();

        // Act
        await hierarchy
            .ForScope(scope)
            .InsertRootAsync(root, treeId, cancellationToken);

        await hierarchy
            .ForScope(scope)
            .InsertChildAsync(child, root.Id, cancellationToken);

        await hierarchy
            .ForScope(scope)
            .InsertRootAsync(otherTree, otherTreeId, cancellationToken);

        await hierarchy
            .ForScope(otherScope)
            .InsertRootAsync(otherPartition, treeId, cancellationToken);

        // WHY: Explicit mutations require affected nodes to be untracked; the coordinated save owns tracking.
        var trackedRoot = await context
            .Set<GuidBinaryStorageNode>()
            .SingleAsync(node => node.Id == root.Id, cancellationToken);

        trackedRoot.Name = "Updated";
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            cancellationToken);

        // Assert
        var rows = await hierarchy
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        Assert.Equal(new[] { root.Id, child.Id }, rows.Select(node => node.Id));
        Assert.All(rows, node => Assert.Equal((scope, treeId), (node.Scope, node.TreeId)));
        Assert.Null(rows[0].ParentId);
        Assert.Equal(root.Id, rows[1].ParentId);
        Assert.Equal("Updated", rows[0].Name);
        Assert.Equal((1L, 4L, 0, 0L), (rows[0].Left, rows[0].Right, rows[0].Depth, rows[0].Position));
        Assert.Equal((2L, 3L, 1, 0L), (rows[1].Left, rows[1].Right, rows[1].Depth, rows[1].Position));
        Assert.Equal(4, trackedRoot.Right);
        Assert.Equal(
            otherTree.Id,
            (await hierarchy
                .ForScope(scope)
                .InTree(otherTreeId)
                .Nodes
                .SingleAsync(cancellationToken)).Id);

        Assert.Equal(
            otherPartition.Id,
            (await hierarchy
                .ForScope(otherScope)
                .InTree(treeId)
                .Nodes
                .SingleAsync(cancellationToken)).Id);

        Assert.True(
            (await hierarchy
                .ForScope(scope)
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken)).IsValid);

        var entity = context.Model.FindEntityType(typeof(GuidBinaryStorageNode))!;
        Assert.All(
            new[] { nameof(root.Id), nameof(root.Scope), nameof(root.TreeId), nameof(root.ParentId) },
            name => Assert.Equal(
                converted ? "varbinary(16)" : "binary(16)",
                entity.FindProperty(name)!.GetColumnType()));

        var mapping = entity.FindProperty(nameof(root.Id))!.GetRelationalTypeMapping();
        Assert.Equal(converted ? typeof(byte[]) : typeof(Guid), mapping.Converter?.ProviderClrType ?? typeof(Guid));
    }

    /// <summary>Scalar and JSON rowsets preserve exact identities and roll back their reservations.</summary>
    /// <param name="converted">Whether the registry identities use EF's Guid-to-byte-array converter.</param>
    /// <param name="count">The final scalar size or first JSON rowset size.</param>
    [Theory]
    [InlineData(false, 255)]
    [InlineData(false, 256)]
    [InlineData(true, 255)]
    [InlineData(true, 256)]
    public async Task AcquireAsync_BinaryGuidRowsets_PreserveDistinctIdentitiesAndRollback(
        bool converted,
        int count
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = new UnicodeIdentityCommandProbe();
        await using var context = await CreateContextAsync(converted, probe, cancellationToken);
        var entity = context.Model.FindEntityType(typeof(GuidBinaryStorageNode))!;
        var scope = Guid.NewGuid();
        var otherScope = Guid.NewGuid();
        var firstTree = Guid.NewGuid();
        var requests = Enumerable
            .Range(0, count)
            .Select(index => (INestedSetTreeLockRequest)new NestedSetTreeLockRequest<Guid, Guid>(
                entity,
                index == 1 ? otherScope : scope,
                index < 2 ? firstTree : Guid.NewGuid(),
                NestedSetTreeLockMode.New))
            .ToArray();

        var registry = NestedSetTreeRegistryMapping.For(entity).Registry;
        var query = context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Where(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.Scope) == scope
                || EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.Scope) == otherScope);

        probe.Reset();

        // Act
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await NestedSetTreeLocks.RequireDistinctAsync(context, requests, cancellationToken);
        await NestedSetTreeLocks.AcquireAsync(context, requests, cancellationToken);
        var jsonCommands = probe.JsonRowsetCommands;
        var actual = await query
            .Select(row => new
            {
                Scope = EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.Scope),
                TreeId = EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
            })
            .ToArrayAsync(cancellationToken);

        await transaction.RollbackAsync(cancellationToken);

        // Assert
        var expected = requests.Select(request => ((Guid)request.ScopeValue!, (Guid)request.TreeIdValue)).ToHashSet();
        Assert.Equal(count, actual.Length);
        Assert.True(expected.SetEquals(actual.Select(row => (row.Scope, row.TreeId))));
        Assert.Equal(count == 256 ? 2 : 0, jsonCommands);
        Assert.Equal(0, await query.CountAsync(cancellationToken));
        _output.WriteLine(
            $"Engine={Engine}; converted={converted}; identities={count}; JSON_TABLE commands={jsonCommands}; "
            + $"exact rows={actual.Length}; rows after rollback=0.");
    }

    /// <summary>Duplicate complete identities fail before either transport can reserve a registry row.</summary>
    /// <param name="converted">Whether the registry identities use EF's Guid-to-byte-array converter.</param>
    /// <param name="count">The final scalar size or first JSON rowset size.</param>
    [Theory]
    [InlineData(false, 255)]
    [InlineData(false, 256)]
    [InlineData(true, 255)]
    [InlineData(true, 256)]
    public async Task RequireDistinctAsync_DuplicateBinaryGuidIdentities_RejectBeforeReservation(
        bool converted,
        int count
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var probe = new UnicodeIdentityCommandProbe();
        await using var context = await CreateContextAsync(converted, probe, cancellationToken);
        var entity = context.Model.FindEntityType(typeof(GuidBinaryStorageNode))!;
        var scope = Guid.NewGuid();
        var duplicate = Guid.NewGuid();
        var requests = Enumerable
            .Range(0, count)
            .Select(index => (INestedSetTreeLockRequest)new NestedSetTreeLockRequest<Guid, Guid>(
                entity,
                scope,
                index < 2 ? duplicate : Guid.NewGuid(),
                NestedSetTreeLockMode.New))
            .ToArray();

        var registry = NestedSetTreeRegistryMapping.For(entity).Registry;
        probe.Reset();

        // Act
        var failure = await Record.ExceptionAsync(() => NestedSetTreeLocks.RequireDistinctAsync(
            context,
            requests,
            cancellationToken));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(count == 256 ? 1 : 0, probe.JsonRowsetCommands);
        Assert.Equal(
            0,
            await context
                .Set<NestedSetTreeRegistry>(registry.Name)
                .AsNoTracking()
                .CountAsync(
                    row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.Scope) == scope,
                    cancellationToken));

        _output.WriteLine(
            $"Engine={Engine}; converted={converted}; requested={count}; duplicate rejected before reservation.");
    }

    /// <summary>Creates either model using the existing fixture without introducing another database owner.</summary>
    /// <param name="converted">Whether to select the converted Guid model.</param>
    /// <param name="probe">The interceptor observing the actual identity transport.</param>
    /// <param name="cancellationToken">Cancels context and schema initialization.</param>
    /// <returns>The initialized context owned by the calling test.</returns>
    private async Task<GuidBinaryStorageContext> CreateContextAsync(
        bool converted,
        UnicodeIdentityCommandProbe probe,
        CancellationToken cancellationToken
    )
    {
        if (converted)
        {
            return await _fixture.CreateContextAsync<ConvertedGuidBinaryStorageContext>(
                Engine,
                static options => new ConvertedGuidBinaryStorageContext(options),
                cancellationToken,
                probe);
        }

        return await _fixture.CreateContextAsync<GuidBinaryStorageContext>(
            Engine,
            static options => new GuidBinaryStorageContext(options),
            cancellationToken,
            probe);
    }
}
