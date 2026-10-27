using System.Globalization;
using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionVisibilityTraversalTests
{
    /// <summary>Models custom reflection metadata that can change its reported query modifier between visits.</summary>
    private sealed class QueryMethodProbe(bool rejectSecondVisit) : MethodInfo
    {
        private bool _observing;

        /// <summary>Gets the number of metadata inspections after the source was constructed.</summary>
        internal int DeclaringTypeReads { get; private set; }

        /// <summary>Starts the observation after expression factories have validated the method metadata.</summary>
        internal void Reset()
        {
            DeclaringTypeReads = 0;
            _observing = true;
        }

        /// <summary>Retains an actual Where call whose predicate can contain a controlled test extension.</summary>
        internal MethodCallExpression Where(Expression source, Expression predicate)
        {
            var node = Expression.Parameter(typeof(CompositionGuardNode), "node");

            return Expression.Call(this, source,
                Expression.Quote(Expression.Lambda<Func<CompositionGuardNode, bool>>(predicate, node)));
        }

        /// <inheritdoc />
        public override Type? DeclaringType
        {
            get
            {
                if (_observing)
                {
                    DeclaringTypeReads++;
                }

                return rejectSecondVisit && DeclaringTypeReads > 1
                    ? typeof(EntityFrameworkQueryableExtensions)
                    : s_where.DeclaringType;
            }
        }

        /// <inheritdoc />
        public override MethodAttributes Attributes => s_where.Attributes;

        /// <inheritdoc />
        public override RuntimeMethodHandle MethodHandle => s_where.MethodHandle;

        /// <inheritdoc />
        public override string Name => rejectSecondVisit && DeclaringTypeReads > 1
            ? nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
            : s_where.Name;

        /// <inheritdoc />
        public override Type? ReflectedType => s_where.ReflectedType;

        /// <inheritdoc />
        public override Type ReturnType => s_where.ReturnType;

        /// <inheritdoc />
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => s_where.ReturnTypeCustomAttributes;

        /// <inheritdoc />
        public override bool IsGenericMethod => s_where.IsGenericMethod;

        /// <inheritdoc />
        public override bool IsGenericMethodDefinition => s_where.IsGenericMethodDefinition;

        /// <inheritdoc />
        public override bool ContainsGenericParameters => s_where.ContainsGenericParameters;

        /// <inheritdoc />
        public override MethodInfo GetBaseDefinition() => s_where.GetBaseDefinition();

        /// <inheritdoc />
        public override MethodImplAttributes GetMethodImplementationFlags() => s_where.GetMethodImplementationFlags();

        /// <inheritdoc />
        public override ParameterInfo[] GetParameters() => s_where.GetParameters();

        /// <inheritdoc />
        public override Type[] GetGenericArguments() => s_where.GetGenericArguments();

        /// <inheritdoc />
        public override MethodInfo GetGenericMethodDefinition() => s_where.GetGenericMethodDefinition();

        /// <inheritdoc />
        public override MethodInfo MakeGenericMethod(params Type[] typeArguments)
            => s_where.MakeGenericMethod(typeArguments);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(bool inherit) => s_where.GetCustomAttributes(inherit);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            => s_where.GetCustomAttributes(attributeType, inherit);

        /// <inheritdoc />
        public override bool IsDefined(Type attributeType, bool inherit) => s_where.IsDefined(attributeType, inherit);

        /// <inheritdoc />
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture)
            => throw new InvalidOperationException("A deferred visibility proof must never execute this operator.");
    }

    /// <summary>Models custom reflection metadata that changes a member's reported type between visits.</summary>
    private sealed class QueryMemberProbe(bool rejectSecondVisit) : PropertyInfo
    {
        private readonly PropertyInfo _property = typeof(MetadataHolder).GetProperty(nameof(MetadataHolder.Allowed))!;
        private bool _observing;

        /// <summary>Gets the number of type inspections after expression construction.</summary>
        internal int TypeReads { get; private set; }

        /// <summary>Starts observation after expression factories have validated the initial bool property.</summary>
        internal void Reset()
        {
            TypeReads = 0;
            _observing = true;
        }

        /// <inheritdoc />
        public override Type PropertyType
        {
            get
            {
                if (_observing)
                {
                    TypeReads++;
                }

                return rejectSecondVisit && TypeReads > 1 ? typeof(IQueryable<CompositionGuardNode>) : typeof(bool);
            }
        }

        /// <inheritdoc />
        public override PropertyAttributes Attributes => _property.Attributes;

        /// <inheritdoc />
        public override bool CanRead => _property.CanRead;

        /// <inheritdoc />
        public override bool CanWrite => _property.CanWrite;

        /// <inheritdoc />
        public override Type? DeclaringType => _property.DeclaringType;

        /// <inheritdoc />
        public override string Name => _property.Name;

        /// <inheritdoc />
        public override Type? ReflectedType => _property.ReflectedType;

        /// <inheritdoc />
        public override MethodInfo[] GetAccessors(bool nonPublic) => _property.GetAccessors(nonPublic);

        /// <inheritdoc />
        public override MethodInfo? GetGetMethod(bool nonPublic) => _property.GetGetMethod(nonPublic);

        /// <inheritdoc />
        public override MethodInfo? GetSetMethod(bool nonPublic) => _property.GetSetMethod(nonPublic);

        /// <inheritdoc />
        public override ParameterInfo[] GetIndexParameters() => _property.GetIndexParameters();

        /// <inheritdoc />
        public override object[] GetCustomAttributes(bool inherit) => _property.GetCustomAttributes(inherit);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            => _property.GetCustomAttributes(attributeType, inherit);

        /// <inheritdoc />
        public override bool IsDefined(Type attributeType, bool inherit) => _property.IsDefined(attributeType, inherit);

        /// <inheritdoc />
        public override object? GetValue(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? index, CultureInfo? culture)
            => throw new InvalidOperationException("A visibility proof must not read application property values.");

        /// <inheritdoc />
        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder,
            object?[]? index, CultureInfo? culture)
            => throw new InvalidOperationException("A visibility proof must not write application property values.");
    }

    /// <summary>Exposes a real CLR property whose getter must remain outside deferred syntax analysis.</summary>
    private sealed class MetadataHolder
    {
        /// <summary>Gets the number of attempted CLR getter calls.</summary>
        internal int GetterReads { get; private set; }

        /// <summary>Gets a bool only if application code explicitly invokes the getter.</summary>
        public bool Allowed
        {
            get
            {
                GetterReads++;

                return true;
            }
        }
    }

    /// <summary>Models custom dispatch that bypasses VisitExtension while exposing a different second child.</summary>
    private sealed class ChangingDispatch(Expression secondVisit) : Expression
    {
        /// <summary>Gets the number of actual Accept dispatches, including repeated references.</summary>
        internal int Visits { get; private set; }

        /// <inheritdoc />
        public override ExpressionType NodeType => ExpressionType.Extension;

        /// <inheritdoc />
        public override Type Type => typeof(bool);

        /// <inheritdoc />
        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Visits++;

            return visitor.Visit(Visits > 1 ? secondVisit : Constant(true));
        }
    }

    /// <summary>Models an opaque extension whose second traversal may expose a query-wide unsafe modifier.</summary>
    private sealed class ChangingPredicate(Expression secondVisit) : Expression
    {
        /// <summary>Gets the number of actual extension traversals, including repeated references.</summary>
        internal int Visits { get; private set; }

        /// <inheritdoc />
        public override ExpressionType NodeType => ExpressionType.Extension;

        /// <inheritdoc />
        public override Type Type => typeof(bool);

        /// <inheritdoc />
        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            Visits++;
            visitor.Visit(Visits > 1 ? secondVisit : Constant(true));

            return this;
        }
    }

    /// <summary>Detects accidental application code execution while inspecting a captured query.</summary>
    private sealed class QueryGetterTrap
    {
        /// <summary>Gets the number of attempted application getter evaluations.</summary>
        internal int Reads { get; private set; }

        /// <summary>Exposes an unsafe application getter that syntax analysis must never invoke.</summary>
        public IQueryable<CompositionGuardNode> Query
        {
            get
            {
                Reads++;

                throw new InvalidOperationException("A visibility proof must not evaluate application getters.");
            }
        }
    }
}
