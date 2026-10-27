using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class DokaStraightJoinTests
{
    private static object? s_opaqueQuery;

    /// <summary>The exact configured public inner join retains each real visible endpoint.</summary>
    /// <param name="selectInner">Whether the selector chooses the inner alias.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedPublicJoinRetainsBothMappedEndpointIdentities(bool selectInner)
    {
        // Arrange
        using var context = CompositionGuardContext.Create();
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping).AsQueryable();
        var method = NestedSetTestAdapters.ForEngine("MySql").OrderedJoinMethod!;
        var query = PublicJoinExpression(nodes, nodes, method);
        Expression<Func<StraightEndpointRow, CompositionGuardNode>> endpoint = selectInner
            ? row => row.Related : row => row.Source;

        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var resolution = NestedSetQueryEndpoint.Require(query, endpoint, mapping, nodes.Expression, "node",
            orderedJoinMethod: method);

        // Assert
        Assert.Same(mapping, resolution.EntityType);
        Assert.True(resolution.IsVisible);
    }

    /// <summary>Query-wide filter modifiers in either input retain membership for either selected endpoint.</summary>
    /// <param name="modifiedInner">Whether the modifier occurs in the inner input.</param>
    /// <param name="selectInner">Whether the selected endpoint is the inner alias.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EitherSourceFilterModifierRetainsMembership(bool modifiedInner, bool selectInner)
    {
        // Arrange
        using var context = CompositionGuardContext.Create();
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping).AsQueryable();
        var method = NestedSetTestAdapters.ForEngine("MySql").OrderedJoinMethod!;
        var outer = modifiedInner ? nodes : nodes.IgnoreQueryFilters();
        var inner = modifiedInner ? nodes.IgnoreQueryFilters() : nodes;
        var query = PublicJoinExpression(outer, inner, method);
        Expression<Func<StraightEndpointRow, CompositionGuardNode>> endpoint = selectInner
            ? row => row.Related : row => row.Source;

        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var resolution = NestedSetQueryEndpoint.Require(query, endpoint, mapping, nodes.Expression, "node",
            orderedJoinMethod: method);

        // Assert
        Assert.Same(mapping, resolution.EntityType);
        Assert.False(resolution.IsVisible);
    }

    /// <summary>Opaque payload queries cannot acquire visibility through a verified join or execute a getter.</summary>
    /// <param name="carrier">The application member hiding an uninspectable query.</param>
    [Theory]
    [InlineData("field")]
    [InlineData("property")]
    [InlineData("static-field")]
    public void OpaquePayloadInputRetainsMembershipWithoutEvaluatingGetters(string carrier)
    {
        // Arrange
        using var context = CompositionGuardContext.Create();
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping).AsQueryable();
        var method = NestedSetTestAdapters.ForEngine("MySql").OrderedJoinMethod!;
        var holder = new OpaqueQueryCarrier { Query = nodes.IgnoreQueryFilters() };
        s_opaqueQuery = nodes.IgnoreQueryFilters();
        Expression<Func<IQueryable<CompositionGuardNode>>> captured = carrier switch
        {
            "field" => () => (IQueryable<CompositionGuardNode>)holder.Query,
            "property" => () => (IQueryable<CompositionGuardNode>)holder.Property,
            _ => () => (IQueryable<CompositionGuardNode>)s_opaqueQuery!,
        };

        var inner = nodes.Provider.CreateQuery<CompositionGuardNode>(captured.Body);
        var query = PublicJoinExpression(nodes, inner, method);
        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var resolution = NestedSetQueryEndpoint.Require(query, row => row.Source, mapping, nodes.Expression, "node",
            orderedJoinMethod: method);

        s_opaqueQuery = null;

        // Assert
        Assert.Same(mapping, resolution.EntityType);
        Assert.False(resolution.IsVisible);
        Assert.Equal(0, holder.Reads);
    }

    /// <summary>A matching public name in another runtime assembly is not the configured operator.</summary>
    /// <param name="provideTrustedMethod">Whether a different validated assembly supplies the trusted method.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnverifiedOrSameNameForeignJoinCannotSupplyEndpointProvenance(bool provideTrustedMethod)
    {
        // Arrange
        using var context = CompositionGuardContext.Create();
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping).AsQueryable();
        var declared = provideTrustedMethod
            ? ForeignOrderedJoinIdentity()
            : NestedSetTestAdapters.ForEngine("MySql").OrderedJoinMethod!;

        var trusted = provideTrustedMethod ? NestedSetTestAdapters.ForEngine("MySql").OrderedJoinMethod : null;

        var query = PublicJoinExpression(nodes, nodes, declared);
        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var error = Record.Exception(() => NestedSetQueryEndpoint.Require(query, row => row.Source,
            mapping, nodes.Expression, "node", orderedJoinMethod: trusted));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>A verified join cannot make a different named hierarchy a valid selected endpoint.</summary>
    [Fact]
    public void InnerAliasFromAnotherNamedMappingIsRejected()
    {
        // Arrange
        using var context = CompositionGuardContext.Create();
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping).AsQueryable();
        var other = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var method = NestedSetTestAdapters.ForEngine("MySql").OrderedJoinMethod!;
        var query = PublicJoinExpression(nodes, other, method);
        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var error = Record.Exception(() => NestedSetQueryEndpoint.Require(query, row => row.Related,
            mapping, nodes.Expression, "node", orderedJoinMethod: method));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>
    /// Retains the real public call for syntax inspection without invoking or translating a synthetic API.
    /// </summary>
    private static IQueryable<StraightEndpointRow> PublicJoinExpression(
        IQueryable<CompositionGuardNode> outer,
        IQueryable<CompositionGuardNode> inner,
        MethodInfo method
    )
    {
        Expression<Func<CompositionGuardNode, int>> key = node => node.Id;
        Expression<Func<CompositionGuardNode, CompositionGuardNode, StraightEndpointRow>> projection =
            (left, right) => new StraightEndpointRow { Source = left, Related = right };

        var call = Expression.Call(method.MakeGenericMethod(typeof(CompositionGuardNode), typeof(CompositionGuardNode),
                typeof(int), typeof(StraightEndpointRow)),
            outer.Expression, inner.Expression, Expression.Quote(key), Expression.Quote(key),
            Expression.Quote(projection));

        return outer.Provider.CreateQuery<StraightEndpointRow>(call);
    }

    /// <summary>Returns a same-name, same-signature operator that is not the selected provider's identity.</summary>
    private static MethodInfo ForeignOrderedJoinIdentity()
    {
        Func<IQueryable<int>, IQueryable<int>, Expression<Func<int, int>>, Expression<Func<int, int>>,
            Expression<Func<int, int, int>>, IQueryable<int>> method = ForeignJoinOperators.StraightJoin;

        return method.Method.GetGenericMethodDefinition();
    }

    /// <summary>Supplies an adversarial marker whose matching public name never proves endpoint provenance.</summary>
    private static class ForeignJoinOperators
    {
        /// <summary>Marks a foreign operator that endpoint validation must reject without executing.</summary>
        /// <typeparam name="TOuter">The outer source row type.</typeparam>
        /// <typeparam name="TInner">The inner source row type.</typeparam>
        /// <typeparam name="TKey">The selected key type.</typeparam>
        /// <typeparam name="TResult">The result row type.</typeparam>
        /// <param name="outer">The outer source.</param>
        /// <param name="inner">The inner source.</param>
        /// <param name="outerKey">The outer key selector.</param>
        /// <param name="innerKey">The inner key selector.</param>
        /// <param name="projection">The pair projection.</param>
        /// <returns>No value because this marker must not execute.</returns>
        public static IQueryable<TResult> StraightJoin<TOuter, TInner, TKey, TResult>(
            IQueryable<TOuter> outer,
            IQueryable<TInner> inner,
            Expression<Func<TOuter, TKey>> outerKey,
            Expression<Func<TInner, TKey>> innerKey,
            Expression<Func<TOuter, TInner, TResult>> projection
        ) => throw new InvalidOperationException("Endpoint validation must not execute a foreign marker.");
    }

    /// <summary>Retains independently selected mapped aliases through the public inner-join projection.</summary>
    private sealed class StraightEndpointRow
    {
        /// <summary>Gets the outer mapped alias.</summary>
        public required CompositionGuardNode Source { get; init; }

        /// <summary>Gets the inner mapped alias.</summary>
        public required CompositionGuardNode Related { get; init; }
    }

    /// <summary>Provides ordinary object carriers that must never be evaluated by visibility proof.</summary>
    private sealed class OpaqueQueryCarrier
    {
        /// <summary>Stores an application query behind its declared object type.</summary>
        public required object Query;

        /// <summary>Gets how often the application getter was evaluated.</summary>
        public int Reads { get; private set; }

        /// <summary>Rejects evaluation instead of hiding application execution in endpoint validation.</summary>
        public object Property
        {
            get
            {
                Reads++;

                throw new InvalidOperationException("Endpoint proof must not execute this application getter.");
            }
        }
    }
}
