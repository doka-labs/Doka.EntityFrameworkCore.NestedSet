using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Only complete equivalent bindings share references; model, named mapping, parameter and literal changes do not.</summary>
    /// <param name="difference">The binding component that differs from the first normalized root.</param>
    /// <param name="same">Whether the complete safe binding should be canonicalized.</param>
    [Theory]
    [InlineData("none", true)]
    [InlineData("parameter", false)]
    [InlineData("literal", false)]
    [InlineData("mapping", false)]
    [InlineData("model", false)]
    public async Task NormalizedOwnedRootsPreserveEveryBindingIdentity(string difference, bool same)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<CompositionGuardContext>("Sqlite", "Data Source=:memory:");
        await using var provider = SelectionServices(options);
        await using var otherProvider = SelectionServices(options);
        await using var context = new CompositionGuardContext(
            new DbContextOptionsBuilder<CompositionGuardContext>(options).UseInternalServiceProvider(provider).Options);

        await using var other = new CompositionGuardContext(
            new DbContextOptionsBuilder<CompositionGuardContext>(options).UseInternalServiceProvider(otherProvider).Options);

        var entity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var secondEntity = difference switch
        {
            "model" => other.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!,
            "mapping" => context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!,
            _ => entity,
        };

        var marker = RootMarker(context);
        var first = WithNormalizedBinding(marker,
            NormalizedBinding(entity, new QueryParameterExpression("scope", typeof(int)), 0, null), entity);

        var second = WithNormalizedBinding(marker,
            NormalizedBinding(secondEntity,
                new QueryParameterExpression(difference == "parameter" ? "anotherScope" : "scope", typeof(int)),
                difference == "literal" ? 1 : 0, null), secondEntity);

        var roots = new NestedSetNormalizedQueryRoots();

        // Act
        var canonicalFirst = roots.Canonicalize(first);
        var canonicalSecond = roots.Canonicalize(second);

        // Assert
        Assert.NotSame(first.Arguments[0], second.Arguments[0]);
        Assert.Equal(same, ReferenceEquals(canonicalFirst.Arguments[0], canonicalSecond.Arguments[0]));
        Assert.Equal(difference != "mapping" && difference != "model",
            ReferenceEquals(canonicalFirst.Arguments[1], canonicalSecond.Arguments[1]));
    }

    /// <summary>Opaque syntax, metadata and literal comparison callbacks never enter normalized-root comparison.</summary>
    /// <param name="shape">The application-owned callback hidden inside an otherwise ordinary binding.</param>
    [Theory]
    [InlineData("object")]
    [InlineData("struct")]
    [InlineData("array")]
    [InlineData("extension")]
    [InlineData("parameter-subclass")]
    [InlineData("constructor-metadata")]
    [InlineData("index-metadata")]
    [InlineData("constant-type")]
    public async Task NormalizedRootAdmissionNeverInvokesApplicationCallbacks(string shape)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<CompositionGuardContext>("Sqlite", "Data Source=:memory:");
        await using var provider = SelectionServices(options);
        await using var context = new CompositionGuardContext(
            new DbContextOptionsBuilder<CompositionGuardContext>(options).UseInternalServiceProvider(provider).Options);

        var entity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var probe = new MetadataReadProbe();
        var marker = RootMarker(context);
        Expression? payload = shape switch
        {
            "extension" => new UntrustedBindingExpression(probe),
            "constructor-metadata" => MetadataDecision(MetadataPayload("constructor", custom: true, probe)),
            "index-metadata" => MetadataDecision(MetadataPayload("index", custom: true, probe)),
            "constant-type" => MetadataDecision(MetadataPayload("constant", custom: true, probe)),
            "parameter-subclass" => null,
            _ => LiteralBindingPayload(shape, probe),
        };

        var parameter = shape == "parameter-subclass"
            ? new UntrustedBindingParameter(probe)
            : new QueryParameterExpression("scope", typeof(int));

        var first = WithNormalizedBinding(marker, NormalizedBinding(entity, parameter, 0, payload), entity);
        var second = WithNormalizedBinding(marker, NormalizedBinding(entity, parameter, 0, payload), entity);
        var roots = new NestedSetNormalizedQueryRoots();
        probe.Arm();

        // Act
        var canonicalFirst = roots.Canonicalize(first);
        var canonicalSecond = roots.Canonicalize(second);

        // Assert
        Assert.Equal(0, probe.Reads);
        Assert.Same(first.Arguments[0], canonicalFirst.Arguments[0]);
        Assert.Same(second.Arguments[0], canonicalSecond.Arguments[0]);
        Assert.NotSame(canonicalFirst.Arguments[0], canonicalSecond.Arguments[0]);
    }

    /// <summary>Creates a real library marker before substituting the normalized binding under test.</summary>
    private static MethodCallExpression RootMarker(CompositionGuardContext context)
    {
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping);
        var query = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(17).InTree(Guid.Empty).SelectAncestorsOrSelf(node => nodes.Any(other => other.Id == node.Id),
                (node, owner, distance) => owner.Id);

        return Assert.IsAssignableFrom<MethodCallExpression>(query.Expression);
    }

    /// <summary>Preserves exact method identity and changes only the two roots owned by canonicalization.</summary>
    private static MethodCallExpression WithNormalizedBinding(
        MethodCallExpression marker,
        Expression visible,
        IEntityType entity
    )
    {
        var arguments = marker.Arguments.ToArray();
        arguments[0] = visible;
        arguments[1] = new EntityQueryRootExpression(entity);

        return marker.Update(marker.Object, arguments);
    }

    /// <summary>Builds the small normalized binding grammar without depending on a database or parameter values.</summary>
    private static MethodCallExpression NormalizedBinding(
        IEntityType entity,
        QueryParameterExpression scope,
        int literal,
        Expression? extra
    )
    {
        var node = Expression.Parameter(typeof(CompositionGuardNode), "node");
        Expression condition = Expression.AndAlso(
            Expression.Equal(Expression.Property(node, nameof(CompositionGuardNode.Scope)), scope),
            Expression.GreaterThan(Expression.Property(node, nameof(CompositionGuardNode.Id)), Expression.Constant(literal)));

        if (extra is not null)
        {
            condition = Expression.AndAlso(condition, extra);
        }

        return Expression.Call(typeof(Queryable), nameof(Queryable.Where), [typeof(CompositionGuardNode)],
            new EntityQueryRootExpression(entity),
            Expression.Quote(Expression.Lambda<Func<CompositionGuardNode, bool>>(condition, node)));
    }

    /// <summary>Hides application equality in an object, boxed struct, or structural array literal.</summary>
    private static BinaryExpression LiteralBindingPayload(string shape, MetadataReadProbe probe)
    {
        object value = shape switch
        {
            "object" => new UntrustedBindingLiteral(probe),
            "struct" => new UntrustedBindingValue(probe),
            "array" => new object[] { new UntrustedBindingLiteral(probe) },
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        return Expression.Equal(Expression.Constant(value, typeof(object)), Expression.Constant(value, typeof(object)));
    }

    /// <summary>Exposes equality callbacks that must never run while admitting a binding.</summary>
    private sealed class UntrustedBindingLiteral(MetadataReadProbe probe)
    {
        /// <inheritdoc />
        public override int GetHashCode()
        {
            probe.Read();

            return 0;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            probe.Read();

            return ReferenceEquals(this, obj);
        }
    }

    /// <summary>Prevents value-type status alone from admitting an application-defined equality implementation.</summary>
    private readonly struct UntrustedBindingValue(MetadataReadProbe probe)
    {
        /// <inheritdoc />
        public override int GetHashCode()
        {
            probe.Read();

            return 0;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            probe.Read();

            return false;
        }
    }

    /// <summary>Rejects an extension before visiting children or requesting its reduction.</summary>
    private sealed class UntrustedBindingExpression(MetadataReadProbe probe) : Expression
    {
        /// <inheritdoc />
        public override Type Type => typeof(bool);

        /// <inheritdoc />
        public override ExpressionType NodeType => ExpressionType.Extension;

        /// <inheritdoc />
        public override bool CanReduce => true;

        /// <inheritdoc />
        public override Expression Reduce()
        {
            probe.Read();

            return Expression.Constant(true);
        }

        /// <inheritdoc />
        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            probe.Read();

            return this;
        }
    }

    /// <summary>Shows that inheriting EF's parameter type cannot make application callbacks trusted.</summary>
    private sealed class UntrustedBindingParameter(MetadataReadProbe probe) : QueryParameterExpression("scope", typeof(int))
    {
        /// <inheritdoc />
        public override int GetHashCode()
        {
            probe.Read();

            return base.GetHashCode();
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            probe.Read();

            return base.Equals(obj);
        }
    }
}
