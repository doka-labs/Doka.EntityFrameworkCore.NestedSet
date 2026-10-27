using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

/// <summary>Protects allocation-free fixed-syntax reuse on the real shadow-property reporting model.</summary>
/// <param name="output">Records all measured allocation observations.</param>
[Collection("Allocation measurements")]
public sealed class SignalPathConstructionAllocationTests(ITestOutputHelper output)
{
    /// <summary>Independent bound proofs reuse their literals without retaining another endpoint alias.</summary>
    /// <param name="engine">The real configured dialect; no server discovery or database access is needed.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MariaDb")]
    [InlineData("MySql")]
    public void IndependentPathBuildsReuseFixedSyntaxWithoutRetainingAliases(string engine)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var connection = engine == "Sqlite" ? "Data Source=:memory:"
            : "Server=localhost;Database=path_construction;User ID=unused;Pooling=false";

        var options = new DbContextOptionsBuilder(ModelCompatibilityDatabase.Options<SignalLabContext>(engine, connection))
            .AddInterceptors(boundary, boundary.Commands);

        using var context = new SignalLabContext(options.Options);
        var query = new NestedSetQuery<SignalNode>(context, context.Model.FindEntityType(typeof(SignalNode))!)
            .ForScope(Guid.Parse("17d85da0-221c-4dd0-a51e-000000000001"));

        // WHY: Bind the actual private proof once outside measurement. The measured operation still builds
        // every direct, two-edge and long-path branch rather than a copied approximation of the algorithm.
        var build = typeof(NestedSetQuery<SignalNode>)
            .GetMethod("PathEdgeEvidence", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<Func<Expression, Expression, ParameterExpression, LambdaExpression?, ConditionalExpression>>(query);

        var upper = Expression.Parameter(typeof(SignalNode), "upper");
        var lower = Expression.Parameter(typeof(SignalNode), "lower");
        var row = Expression.Parameter(typeof(object), "row");
        Func<object> operation = () => build(upper, lower, row, null);

        for (var warmup = 0; warmup < 1_000; warmup++)
        {
            GC.KeepAlive(operation());
        }

        var first = build(upper, lower, row, null);
        var otherUpper = Expression.Parameter(typeof(SignalNode), "otherUpper");
        var otherLower = Expression.Parameter(typeof(SignalNode), "otherLower");
        var otherRow = Expression.Parameter(typeof(object), "otherRow");

        // Act
        var second = build(otherUpper, otherLower, otherRow, null);
        var allocated = QueryConstructionAllocation.Measure(operation);

        // Assert
        output.WriteLine($"Complete path: engine={engine}; observed bytes/build={allocated}.");
        var firstLiterals = Literals(first);
        var secondLiterals = Literals(second);

        // WHY: Total bytes include framework construction costs that varied between otherwise identical
        // runs. Reference identity checks the actual saving directly: no new literal object per build.
        // Separate endpoint checks prevent satisfying it by caching a complete caller-bound proof.
        Assert.Same(firstLiterals.Zero, secondLiterals.Zero);
        Assert.Same(firstLiterals.Invalid, secondLiterals.Invalid);
        Assert.Same(firstLiterals.One, secondLiterals.One);
        Assert.Same(firstLiterals.Two, secondLiterals.Two);
        Assert.Equal(0L, Assert.IsAssignableFrom<ConstantExpression>(firstLiterals.Zero).Value);
        Assert.Equal(-1L, Assert.IsAssignableFrom<ConstantExpression>(firstLiterals.Invalid).Value);
        Assert.Equal(1L, Assert.IsAssignableFrom<ConstantExpression>(firstLiterals.One).Value);
        Assert.Equal(2L, Assert.IsAssignableFrom<ConstantExpression>(firstLiterals.Two).Value);
        Assert.NotSame(first, second);
        Assert.True(NestedSetQueryExpressions.References(first, upper));
        Assert.True(NestedSetQueryExpressions.References(first, lower));
        Assert.False(NestedSetQueryExpressions.References(first, otherUpper));
        Assert.False(NestedSetQueryExpressions.References(first, otherLower));
        Assert.True(NestedSetQueryExpressions.References(second, otherUpper));
        Assert.True(NestedSetQueryExpressions.References(second, otherLower));
        Assert.False(NestedSetQueryExpressions.References(second, upper));
        Assert.False(NestedSetQueryExpressions.References(second, lower));
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Reads the proof's scalar choices without constructing replacement nodes or evaluating them.</summary>
    private static (Expression Zero, Expression Invalid, Expression One, Expression Two) Literals(
        ConditionalExpression proof
    )
    {
        var oneEdge = Assert.IsAssignableFrom<ConditionalExpression>(proof.IfFalse);
        var twoEdges = Assert.IsAssignableFrom<ConditionalExpression>(oneEdge.IfFalse);

        return (Assert.IsAssignableFrom<BinaryExpression>(proof.Test).Right,
            Assert.IsAssignableFrom<ConditionalExpression>(proof.IfTrue).IfFalse,
            Assert.IsAssignableFrom<BinaryExpression>(oneEdge.Test).Right,
            Assert.IsAssignableFrom<BinaryExpression>(twoEdges.Test).Right);
    }
}
