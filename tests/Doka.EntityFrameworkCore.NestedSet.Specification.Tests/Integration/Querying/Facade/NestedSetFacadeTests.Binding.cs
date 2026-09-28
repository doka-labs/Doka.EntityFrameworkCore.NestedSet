namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetFacadeTests
{
    /// <summary>A scoped hierarchy rejects query creation before Scope is bound.</summary>
    [Fact]
    public void ScopedHierarchyRequiresForScope()
    {
        // Arrange
        using var context = CreateMetadataContext();
        var hierarchy = context.NestedSet<TreeNode>();

        // Act
        var exception = Record.Exception(() => hierarchy.InTree(s_firstTree));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>A Scope argument whose type differs from the finalized model is rejected immediately.</summary>
    [Fact]
    public void ScopeTypeMustMatchFinalizedModel()
    {
        // Arrange
        using var context = CreateMetadataContext();
        var hierarchy = context.NestedSet<TreeNode>();

        // Act
        var exception = Record.Exception(() => hierarchy.ForScope("7"));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>A scopeless hierarchy rejects an unnecessary Scope binding.</summary>
    [Fact]
    public void ScopelessHierarchyRejectsForScope()
    {
        // Arrange
        using var context = CreateMetadataContext();
        var hierarchy = context.NestedSet<UnscopedQueryNode>();

        // Act
        var exception = Record.Exception(() => hierarchy.ForScope(7));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>An entity that is not configured as a hierarchy is rejected at the standard entry point.</summary>
    [Fact]
    public void NonHierarchyEntityIsRejectedImmediately()
    {
        // Arrange
        using var context = CreateMetadataContext();

        // Act
        var exception = Record.Exception(context.NestedSet<UnrelatedRow>);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>A TreeId argument whose type differs from the finalized model is rejected immediately.</summary>
    [Fact]
    public void TreeIdTypeMustMatchFinalizedModel()
    {
        // Arrange
        using var context = CreateMetadataContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        var exception = Record.Exception(() => hierarchy.InTree("first"));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>A NodeKey argument whose type differs from the finalized model is rejected immediately.</summary>
    [Fact]
    public void NodeKeyTypeMustMatchFinalizedModel()
    {
        // Arrange
        using var context = CreateMetadataContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        var exception = Record.Exception(() => hierarchy.AncestorsOf("3"));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>A mutation cannot begin before a configured Scope is bound.</summary>
    [Fact]
    public async Task MutationRequiresConfiguredScopeBinding()
    {
        // Arrange
        await using var context = CreateMetadataContext();
        var hierarchy = context.NestedSet<TreeNode>();

        // Act
        var exception = await Record.ExceptionAsync(() => hierarchy.InsertRootAsync(
            new TreeNode { NodeId = 1 },
            s_firstTree,
            CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>A mutation TreeId must have the exact finalized model type.</summary>
    [Fact]
    public async Task MutationTreeIdTypeMustMatchFinalizedModel()
    {
        // Arrange
        await using var context = CreateMetadataContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        var exception = await Record.ExceptionAsync(() => hierarchy.InsertRootAsync(
            new TreeNode { NodeId = 1 },
            "first",
            CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>A mutation NodeKey must have the exact finalized model type.</summary>
    [Fact]
    public async Task MutationNodeKeyTypeMustMatchFinalizedModel()
    {
        // Arrange
        await using var context = CreateMetadataContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        var exception = await Record.ExceptionAsync(() => hierarchy.DeleteAsync("1", CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>Creates a provider-configured context without opening a database connection.</summary>
    private static TreeContext CreateMetadataContext()
    {
        var options = new DbContextOptionsBuilder<TreeContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        return new TreeContext(options);
    }
}
