namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies model-only configuration contracts without starting a database.</summary>
public sealed class MappingContractTests
{
    /// <summary>Verifies that a failing callback leaves neither partial annotations nor indexes behind.</summary>
    [Fact]
    public void FailedBuilderCallbackLeavesAnnotationsUnchanged()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<TreeNode>();
        entity.HasKey(x => x.NodeId);
        var before = entity
            .Metadata
            .GetAnnotations()
            .Select(x => (x.Name, x.Value))
            .ToArray();

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(x =>
        {
            x
                .HasBounds(n => n.Start, n => n.End)
                .HasScope(n => n.Tree);

            throw new InjectedCommandException();
        }));

        // Assert
        Assert.IsType<InjectedCommandException>(exception);
        Assert.Equal(
            before,
            entity
                .Metadata
                .GetAnnotations()
                .Select(x => (x.Name, x.Value))
                .ToArray());
        Assert.Empty(entity.Metadata.GetIndexes());
    }

    /// <summary>Verifies that incomplete configuration leaves existing annotations and indexes unchanged.</summary>
    [Fact]
    public void IncompleteBuilderConfigurationLeavesAnnotationsUnchanged()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = model.Entity<TreeNode>();
        entity.HasKey(x => x.NodeId);
        var before = entity
            .Metadata
            .GetAnnotations()
            .Select(x => (x.Name, x.Value))
            .ToArray();

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(x => x.HasScope(n => n.Tree)));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Equal(
            before,
            entity
                .Metadata
                .GetAnnotations()
                .Select(x => (x.Name, x.Value))
                .ToArray());
        Assert.Empty(entity.Metadata.GetIndexes());
    }

    /// <summary>
    /// Verifies structural sibling predicates use the model key collation for stored parent comparisons.
    /// </summary>
    [Fact]
    public void StructuralSiblingPredicateUsesModelKeyCollation()
    {
        // Arrange
        using var context = ContractModels.CreateVariantContext("model-collation");
        var store = new NestedSetStore<TextNode, string, Guid, string>(
            context, context.Model.FindEntityType(typeof(TextNode))!, "scope", Guid.Empty);

        // Act
        var query = store.Siblings(new NestedSetParent<string>(true, "parent")).ToQueryString();

        // Assert
        Assert.Contains("COLLATE NOCASE", query, StringComparison.Ordinal);
    }

    /// <summary>Verifies that a global query filter remains a supported composable result boundary.</summary>
    [Fact]
    public void GlobalQueryFilterIsAccepted()
    {
        // Arrange
        using var context = ContractModels.CreateVariantContext("filter");

        // Act
        var exception = Record.Exception(() => context.NestedSet<TreeNode>().ForScope(1));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>Verifies that supported advanced relational mappings remain valid nested-set models.</summary>
    /// <param name="variant">The supported mapping variation.</param>
    [Theory]
    [InlineData("converted-scope")]
    [InlineData("inheritance")]
    [InlineData("shared-table")]
    public void SupportedAdvancedMappingIsAccepted(
        string variant
    )
    {
        // Arrange
        using var context = ContractModels.CreateVariantContext(variant);

        // Act
        var exception = Record.Exception(() => context.NestedSet<TreeNode>().ForScope(1));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that unsupported integer-key mappings are rejected before a mutation reaches the database.
    /// </summary>
    /// <param name="variant">The unsupported mapping variation.</param>
    [Theory]
    [InlineData("unique-boundary")]
    [InlineData("unique-sibling")]
    [InlineData("unique-depth")]
    [InlineData("generated-boundary")]
    [InlineData("generated-depth")]
    [InlineData("generated-position")]
    [InlineData("converted-key")]
    [InlineData("converted-parent")]
    [InlineData("converted-position")]
    [InlineData("converted-depth")]
    [InlineData("converted-boundary")]
    [InlineData("duplicate-role")]
    [InlineData("duplicate-depth-role")]
    [InlineData("duplicate-position-role")]
    [InlineData("required-parent")]
    [InlineData("cascade")]
    [InlineData("split")]
    public async Task UnsupportedIntegerKeyMappingIsRejected(
        string variant
    )
    {
        // Arrange
        await using var context = ContractModels.CreateVariantContext(variant);

        // Act
        var exception = await Record.ExceptionAsync(() => context.NestedSet<TreeNode>().ForScope(1)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    /// <summary>
    /// Verifies that unsupported string-key mappings are rejected before a mutation reaches the database.
    /// </summary>
    /// <param name="variant">The unsupported mapping variation.</param>
    [Theory]
    [InlineData("nullable-scope")]
    [InlineData("parent-collation-without-key-collation")]
    public async Task UnsupportedStringKeyMappingIsRejected(
        string variant
    )
    {
        // Arrange
        await using var context = ContractModels.CreateVariantContext(variant);

        // Act
        var exception = await Record.ExceptionAsync(() => context.NestedSet<TextNode>().ForScope("scope")
            .InsertRootAsync(new TextNode { Id = "root" }, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    /// <summary>Verifies that keyless entity types fail with an identity-specific diagnostic.</summary>
    [Fact]
    public async Task KeylessMappingHasActionableDiagnostic()
    {
        // Arrange
        await using var context = ContractModels.CreateVariantContext("keyless");

        // Act
        var exception = await Record.ExceptionAsync(() => context.NestedSet<TreeNode>().ForScope(1)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None));

        // Assert
        var error = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("requires an EF primary or alternate key", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies that structural columns split across tables fail with a storage-specific diagnostic.</summary>
    [Fact]
    public async Task DistributedStructureHasActionableDiagnostic()
    {
        // Arrange
        await using var context = ContractModels.CreateVariantContext("split");

        // Act
        var exception = await Record.ExceptionAsync(() => context.NestedSet<TreeNode>().ForScope(1)
            .InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None));

        // Assert
        var error = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("every structural and ordering property", error.Message, StringComparison.Ordinal);
        Assert.Contains("one unambiguous writable table fragment", error.Message, StringComparison.Ordinal);
    }
}
