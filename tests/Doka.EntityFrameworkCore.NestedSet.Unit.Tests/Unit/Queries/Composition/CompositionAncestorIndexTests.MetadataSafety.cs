using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Both classifiers reject application metadata without reading its getters after construction.</summary>
    /// <param name="kind">The custom constructor, index property, or declared constant type.</param>
    /// <param name="parent">The ordinary parent syntax that can forward a child's declared type.</param>
    /// <param name="metadataFirst">Whether the metadata payload precedes the independent fact selection.</param>
    [Theory]
    [InlineData("constructor", "direct", false)]
    [InlineData("constructor", "conditional", false)]
    [InlineData("constructor", "block", false)]
    [InlineData("constructor", "assign", false)]
    [InlineData("index", "direct", false)]
    [InlineData("index", "conditional", false)]
    [InlineData("index", "block", false)]
    [InlineData("index", "assign", false)]
    [InlineData("constant", "direct", false)]
    [InlineData("constant", "conditional", false)]
    [InlineData("constant", "block", false)]
    [InlineData("constant", "assign", false)]
    [InlineData("constructor", "direct", true)]
    [InlineData("index", "direct", true)]
    [InlineData("constant", "direct", true)]
    public async Task ApplicationMetadataGettersRemainOutsideClassification(
        string kind,
        string parent,
        bool metadataFirst
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>("MySql",
            "Server=127.0.0.1;Database=metadata_safety;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options).UseInternalServiceProvider(provider).Options);

        var model = context.Model;
        var probe = new MetadataReadProbe();
        var payload = MetadataPayload(kind, custom: true, probe);
        var decision = MetadataDecision(WrapMetadataPayload(payload, parent));
        var facts = FactSelection(context.Set<SelectionFact>(), (node, fact) => fact.NodeId == node.Id);
        var scalar = Expression.Lambda<Func<CompositionNode, bool>>(decision, facts.Parameters);
        var combined = Expression.Lambda<Func<CompositionNode, bool>>(
            metadataFirst ? Expression.AndAlso(decision, facts.Body) : Expression.AndAlso(facts.Body, decision),
            facts.Parameters);

        var usesQueryOperators = false;
        var requiresCorrelation = false;
        var factSelection = true;

        // WHY: Expression factories legitimately validate metadata while building a predicate. Only
        // classifier access after the complete scalar and fact predicates exist is the prohibited callback.
        probe.Arm();

        // Act
        var syntaxError = Record.Exception(() =>
            usesQueryOperators = NestedSetQuerySelection.UsesQueryOperators(scalar));

        var selectionError = Record.Exception(() => requiresCorrelation =
            NestedSetQuerySelection.RequiresAncestorDecorrelation(combined, model, out factSelection));

        // Assert
        Assert.Equal(0, probe.Reads);
        Assert.Null(syntaxError);
        Assert.Null(selectionError);
        Assert.True(usesQueryOperators);
        Assert.True(requiresCorrelation);
        Assert.False(factSelection);
    }

    /// <summary>Ordinary runtime metadata remains eligible beside an explicit independent fact selection.</summary>
    /// <param name="kind">The runtime constructor, index property, or declared constant type.</param>
    /// <param name="parent">The ordinary parent syntax retaining the same scalar contract.</param>
    [Theory]
    [InlineData("constructor", "direct")]
    [InlineData("constructor", "conditional")]
    [InlineData("constructor", "block")]
    [InlineData("constructor", "assign")]
    [InlineData("index", "direct")]
    [InlineData("index", "conditional")]
    [InlineData("index", "block")]
    [InlineData("index", "assign")]
    [InlineData("constant", "direct")]
    [InlineData("constant", "conditional")]
    [InlineData("constant", "block")]
    [InlineData("constant", "assign")]
    public async Task RuntimeMetadataRetainsIndependentFactEligibility(
        string kind,
        string parent
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>("MySql",
            "Server=127.0.0.1;Database=metadata_safety;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options).UseInternalServiceProvider(provider).Options);

        var model = context.Model;
        var probe = new MetadataReadProbe();
        var payload = MetadataPayload(kind, custom: false, probe);
        var decision = MetadataDecision(WrapMetadataPayload(payload, parent));
        var facts = FactSelection(context.Set<SelectionFact>(), (node, fact) => fact.NodeId == node.Id);
        var scalar = Expression.Lambda<Func<CompositionNode, bool>>(decision, facts.Parameters);
        var combined = Expression.Lambda<Func<CompositionNode, bool>>(
            Expression.AndAlso(facts.Body, decision), facts.Parameters);

        probe.Arm();

        // Act
        var usesQueryOperators = NestedSetQuerySelection.UsesQueryOperators(scalar);
        var requiresCorrelation = NestedSetQuerySelection.RequiresAncestorDecorrelation(combined, model,
            out var factSelection);

        // Assert
        Assert.Equal(0, probe.Reads);
        Assert.False(usesQueryOperators);
        Assert.False(requiresCorrelation);
        Assert.True(factSelection);
    }

    /// <summary>Dynamic reduction cannot inspect an application-controlled delegate type.</summary>
    /// <param name="parent">The ordinary parent syntax surrounding the dynamic expression.</param>
    /// <param name="metadataFirst">Whether the dynamic payload precedes the independent fact selection.</param>
    [Theory]
    [InlineData("direct", false)]
    [InlineData("conditional", false)]
    [InlineData("block", false)]
    [InlineData("assign", false)]
    [InlineData("direct", true)]
    public async Task DynamicDelegateMetadataRemainsOpaqueWithoutReduction(
        string parent,
        bool metadataFirst
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>("MySql",
            "Server=127.0.0.1;Database=metadata_safety;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options).UseInternalServiceProvider(provider).Options);

        var model = context.Model;
        var probe = new MetadataReadProbe();
        var delegateType = new DynamicDelegateMetadataProbe(probe);
        var dynamic = Expression.MakeDynamic(delegateType, new MetadataBinderProbe(),
            Expression.Constant(1, typeof(object)));

        var decision = MetadataDecision(WrapMetadataPayload(dynamic, parent));
        var facts = FactSelection(context.Set<SelectionFact>(), (node, fact) => fact.NodeId == node.Id);
        var scalar = Expression.Lambda<Func<CompositionNode, bool>>(decision, facts.Parameters);
        var combined = Expression.Lambda<Func<CompositionNode, bool>>(
            metadataFirst ? Expression.AndAlso(decision, facts.Body) : Expression.AndAlso(facts.Body, decision),
            facts.Parameters);

        var usesQueryOperators = false;
        var requiresCorrelation = false;
        var factSelection = true;
        probe.Arm();

        // Act
        var syntaxError = Record.Exception(() =>
            usesQueryOperators = NestedSetQuerySelection.UsesQueryOperators(scalar));

        var selectionError = Record.Exception(() => requiresCorrelation =
            NestedSetQuerySelection.RequiresAncestorDecorrelation(combined, model, out factSelection));

        // Assert
        Assert.Equal(0, probe.Reads);
        Assert.Null(syntaxError);
        Assert.Null(selectionError);
        Assert.True(usesQueryOperators);
        Assert.True(requiresCorrelation);
        Assert.False(factSelection);
    }

    /// <summary>Creates public factory inputs whose metadata has a matching runtime positive control.</summary>
    private static Expression MetadataPayload(
        string kind,
        bool custom,
        MetadataReadProbe probe
    )
    {
        return kind switch
        {
            "constructor" => Expression.New(custom
                ? new ConstructorMetadataProbe(typeof(object).GetConstructor(Type.EmptyTypes)!, probe)
                : typeof(object).GetConstructor(Type.EmptyTypes)!),
            "index" => Expression.MakeIndex(Expression.Constant(new List<int> { 0 }), custom
                ? new IndexMetadataProbe(typeof(List<int>).GetProperty("Item")!, probe)
                : typeof(List<int>).GetProperty("Item")!, [Expression.Constant(0)]),
            "constant" => Expression.Constant(null, custom ? new ConstantMetadataProbe(probe) : typeof(string)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>Retains real framework parent nodes whose declared type can forward a child's metadata.</summary>
    private static Expression WrapMetadataPayload(
        Expression payload,
        string parent
    )
    {
        var variable = Expression.Variable(payload.Type, "metadataValue");

        return parent switch
        {
            "direct" => payload,
            "conditional" => Expression.Condition(Expression.Constant(true), payload, payload),
            "block" => Expression.Block(Expression.Constant(0), payload),
            "assign" => Expression.Block([variable], Expression.Assign(variable, payload)),
            _ => throw new ArgumentOutOfRangeException(nameof(parent)),
        };
    }

    /// <summary>Embeds each scalar payload into a Boolean predicate without executing or compiling it.</summary>
    private static BinaryExpression MetadataDecision(Expression payload)
        => payload.Type == typeof(int)
            ? Expression.Equal(payload, Expression.Constant(0))
            : Expression.ReferenceNotEqual(payload, Expression.Constant(null, payload.Type));
}
