using System.Linq.Expressions;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.UserGroups;

/// <summary>
/// Protects the sample's static expression optimization from retaining scoped contexts, models, or query roots.
/// </summary>
public sealed class SupervisorRoutingCacheTests
{
    /// <summary>Every cached owner-pair shape contains only expression parameters and immutable owner flags.</summary>
    [Fact]
    public void CachedRoutingShapesContainNoScopedQueryState()
    {
        // Arrange
        var field = typeof(App.SupervisorQueries)
            .GetField("s_routingTemplates", BindingFlags.Static | BindingFlags.NonPublic)!;

        var templates = (Array)field.GetValue(null)!;
        var visitor = new RoutingCacheProbe();

        // Act
        foreach (var template in templates)
        {
            var expression = (LambdaExpression)template!.GetType()
                .GetField("_expression", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(template)!;

            visitor.Visit(expression);
        }

        // Assert
        Assert.Equal(9, templates.Length);
        Assert.Empty(visitor.UnsafeConstants);
        Assert.Equal(0, visitor.QueryExtensions);
        Assert.True(visitor.OwnerFlags > 0);
    }

    /// <summary>
    /// Inspects actual cached constants without creating a test context whose collection depends on GC timing.
    /// </summary>
    private sealed class RoutingCacheProbe : ExpressionVisitor
    {
        /// <summary>Gets unexpected rooted request, query, metadata, or context objects.</summary>
        internal List<object> UnsafeConstants { get; } = [];

        /// <summary>Gets the number of captured immutable owner flags.</summary>
        internal int OwnerFlags { get; private set; }

        /// <summary>
        /// Gets provider query-root or other extension nodes, which must not be stored in a template.
        /// </summary>
        internal int QueryExtensions { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is not null and not string and not bool and not int and not long)
            {
                var fields = node.Value.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (fields.Length == 0
                    || fields.Any(field => field.FieldType != typeof(bool)))
                {
                    UnsafeConstants.Add(node.Value);
                }
                else
                {
                    OwnerFlags += fields.Length;
                }
            }

            return node;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(Expression node)
        {
            QueryExtensions++;

            return node;
        }
    }
}
