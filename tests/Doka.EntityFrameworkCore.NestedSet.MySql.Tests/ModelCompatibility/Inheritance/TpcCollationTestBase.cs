namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Verifies native identity facets of the Doka family's concrete TPC table collations.</summary>
public abstract class TpcCollationTestBase : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares isolated provider schemas while each case allocates independent Scope and key values.</summary>
    /// <param name="fixture">The engine-bound compatibility fixture.</param>
    protected TpcCollationTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Inherited string properties retain a separate effective capture for each physical table.</summary>
    [Fact]
    public void SharedPropertiesRetainConcreteTableCollations()
    {
        // Arrange
        using var context = new CollatedTpcContext(ModelCompatibilityDatabase.Options<CollatedTpcContext>(Engine));
        var binary = context.Model.FindEntityType(typeof(BinaryTpcNode))!;
        var insensitive = context.Model.FindEntityType(typeof(CaseInsensitiveTpcNode))!;
        var names = new[]
        {
            nameof(CollatedTpcNode.Id), nameof(CollatedTpcNode.Scope), nameof(CollatedTpcNode.ParentId),
        };

        // Act
        var captures = names
            .Select(name => (Binary: NestedSetCollations.Resolve(context, binary.FindProperty(name)!, binary),
                Insensitive: NestedSetCollations.Resolve(context, insensitive.FindProperty(name)!, insensitive)))
            .ToArray();

        // Assert
        Assert.All(names, name => Assert.Same(binary.FindProperty(name), insensitive.FindProperty(name)));
        Assert.All(captures, capture => Assert.Equal(("utf8mb4_bin", "utf8mb4_unicode_ci"), capture));
        var binaryMapping = NestedSetMapping<BinaryTpcNode, string, string>.For(context, binary);
        var insensitiveMapping = NestedSetMapping<CaseInsensitiveTpcNode, string, string>.For(context, insensitive);
        Assert.Equal("BinaryTpcScopes", binaryMapping.Store.Name);
        Assert.Equal("CaseInsensitiveTpcScopes", insensitiveMapping.Store.Name);
        Assert.Equal("utf8mb4_bin", binaryMapping.KeyCollation);
        Assert.Equal("utf8mb4_unicode_ci", insensitiveMapping.KeyCollation);
    }

    /// <summary>The binary table allows two independent case-distinct Scopes with the same TreeId.</summary>
    [Fact]
    public async Task BinaryConcreteTableCanReserveIndependentScopes()
    {
        // Arrange
        await using var context = await CreateContextAsync(Engine);
        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "BINARY-" + suffix;
        var lower = scope.ToLowerInvariant();
        var treeId = Guid.NewGuid();
        var first = new BinaryTpcNode
        {
            Id = "B1-" + suffix,
            Name = "First",
        };

        await context
            .NestedSet<BinaryTpcNode>()
            .ForScope(scope)
            .InsertRootAsync(first, treeId, CancellationToken.None);

        var before = Snapshot(first);
        context.ChangeTracker.Clear();
        var second = new BinaryTpcNode
        {
            Id = "B2-" + suffix,
            Name = "Second",
        };

        // Act
        await context
            .NestedSet<BinaryTpcNode>()
            .ForScope(lower)
            .InsertRootAsync(second, treeId, CancellationToken.None);

        // Assert
        var persistedFirst = await context
            .NestedSet<BinaryTpcNode>()
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var persistedSecond = await context
            .NestedSet<BinaryTpcNode>()
            .ForScope(lower)
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(before, Snapshot(persistedFirst));
        Assert.Equal(
            (lower, treeId, 1L, 2L, 0),
            (persistedSecond.Scope, persistedSecond.TreeId, persistedSecond.Left, persistedSecond.Right,
                persistedSecond.Depth));
        Assert.Null(persistedSecond.ParentId);
        Assert.Equal(0L, persistedSecond.Position);
        Assert.Equal(second.Id, persistedSecond.Id);
        Assert.Equal(second.Name, persistedSecond.Name);
    }

    /// <summary>The CI table rejects a native Scope alias reserving an already active TreeId.</summary>
    [Fact]
    public async Task CaseInsensitiveConcreteTableRejectsScopeAliasReservation()
    {
        // Arrange
        await using var context = await CreateContextAsync(Engine);
        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "CI-" + suffix;
        var treeId = Guid.NewGuid();
        var first = new CaseInsensitiveTpcNode
        {
            Id = "C1-" + suffix,
            Name = "Original",
        };

        await context
            .NestedSet<CaseInsensitiveTpcNode>()
            .ForScope(scope)
            .InsertRootAsync(first, treeId, CancellationToken.None);

        var before = Snapshot(first);
        context.ChangeTracker.Clear();
        var rejected = new CaseInsensitiveTpcNode
        {
            Id = "C2-" + suffix,
            Scope = scope.ToLowerInvariant(),
            Name = "Retry",
        };

        var inputBefore = Snapshot(rejected);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<CaseInsensitiveTpcNode>()
            .ForScope(rejected.Scope)
            .InsertRootAsync(rejected, treeId, CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.TreeIdUnavailable,
            Assert.IsType<NestedSetException>(failure)
                .Code);
        Assert.Equal(inputBefore, Snapshot(rejected));
        Assert.Equal(
            EntityState.Detached,
            context.Entry(rejected)
                .State);
        var persisted = await context
            .NestedSet<CaseInsensitiveTpcNode>()
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(before, Snapshot(persisted));
    }

    /// <summary>Parent lookup follows the concrete CI principal key despite its shared inherited property.</summary>
    [Fact]
    public async Task CaseInsensitiveConcreteParentAcceptsKeyAlias()
    {
        // Arrange
        await using var context = await CreateContextAsync(Engine);
        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "parent-ci-" + suffix;
        var treeId = Guid.NewGuid();
        var root = new CaseInsensitiveTpcNode
        {
            Id = "PARENT-" + suffix,
            Name = "Root",
        };
        var hierarchy = context
            .NestedSet<CaseInsensitiveTpcNode>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        var child = new CaseInsensitiveTpcNode
        {
            Id = "CHILD-" + suffix,
            Name = "Child",
        };

        // Act
        await hierarchy.InsertChildAsync(child, root.Id.ToLowerInvariant(), CancellationToken.None);

        // Assert
        var parent = await hierarchy
            .ParentOf(child.Id)
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);
        Assert.Equal(root.Id, parent.Id);
        Assert.Equal(
            (scope, treeId, 2L, 3L, 1, 0L),
            (child.Scope, child.TreeId, child.Left, child.Right, child.Depth, child.Position));
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal((1L, 4L), (parent.Left, parent.Right));
    }

    /// <summary>Parent lookup cannot reuse CI semantics from the other concrete table.</summary>
    [Fact]
    public async Task BinaryConcreteParentRejectsKeyAlias()
    {
        // Arrange
        await using var context = await CreateContextAsync(Engine);
        var suffix = Guid
            .NewGuid()
            .ToString("N");

        var scope = "parent-bin-" + suffix;
        var treeId = Guid.NewGuid();
        var root = new BinaryTpcNode
        {
            Id = "PARENT-" + suffix,
            Name = "Root",
        };

        var hierarchy = context
            .NestedSet<BinaryTpcNode>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(root, treeId, CancellationToken.None);
        var before = Snapshot(root);
        var child = new BinaryTpcNode
        {
            Id = "CHILD-" + suffix,
            Name = "Child",
        };

        var inputBefore = Snapshot(child);

        // Act
        var failure = await Record.ExceptionAsync(() => hierarchy.InsertChildAsync(
            child,
            root.Id.ToLowerInvariant(),
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.NodeNotFound,
            Assert.IsType<NestedSetException>(failure)
                .Code);
        Assert.Equal(inputBefore, Snapshot(child));
        Assert.Equal(
            EntityState.Detached,
            context.Entry(child)
                .State);
        var persisted = await hierarchy
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(before, Snapshot(persisted));
    }

    /// <summary>Shares one isolated schema while every test selects new Scope and assigned key values.</summary>
    private Task<CollatedTpcContext> CreateContextAsync(
        string engine
    ) => _fixture.CreateContextAsync<CollatedTpcContext>(engine, static options => new CollatedTpcContext(options));

    /// <summary>Captures every hierarchy field and payload for exact preservation assertions.</summary>
    private static (string Id, string Scope, Guid TreeId, string? Parent, long Left, long Right, int Depth, long
        Position, string Name) Snapshot(
            CollatedTpcNode node
        ) => (node.Id, node.Scope, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth, node.Position,
        node.Name);
}
