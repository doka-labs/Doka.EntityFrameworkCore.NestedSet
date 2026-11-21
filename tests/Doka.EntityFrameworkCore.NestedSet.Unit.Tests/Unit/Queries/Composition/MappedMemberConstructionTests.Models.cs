using System.Globalization;
using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class MappedMemberConstructionTests
{
    /// <summary>Exposes metadata without ever permitting query construction to execute a CLR getter.</summary>
    private class MemberNode
    {
        /// <summary>Supplies a real field for changing the mutable model's storage mapping.</summary>
        public int Stored = -1;

        /// <summary>Throws if a query builder accidentally reads the application instance.</summary>
        public int Value => throw new InvalidOperationException($"{GetType().Name}.Value must remain deferred.");

        /// <summary>Supplies an alternative member name for the mutable reflection counterexample.</summary>
        public int Other => throw new InvalidOperationException($"{GetType().Name}.Other must remain deferred.");
    }

    /// <summary>Provides a compatible but distinct alias type for the same mapped CLR declaration.</summary>
    private sealed class DerivedMemberNode : MemberNode;

    /// <summary>Provides an incompatible alias with no relation to the mapped CLR declaration.</summary>
    private sealed class UnrelatedNode;

    /// <summary>Lets EF initialize an independent empty runtime model through its ordinary context services.</summary>
    private sealed class ModelContext : DbContext
    {
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ModelContext(
            DbContextOptions<ModelContext> options
        ) : base(options) { }
    }

    /// <summary>
    /// Exposes observable Type reads while behaving as a readable expression with an ordinary CLR type.
    /// </summary>
    private sealed class TypeObservationExpression : Expression
    {
        /// <summary>Gets the number of times query construction observed this expression's CLR type.</summary>
        internal int TypeReads { get; private set; }

        /// <inheritdoc />
        public override ExpressionType NodeType => ExpressionType.Extension;

        /// <inheritdoc />
        public override Type Type
        {
            get
            {
                TypeReads++;

                return typeof(MemberNode);
            }
        }
    }

    /// <summary>
    /// Wraps metadata to prove that custom implementations retain their original per-call inspection.
    /// </summary>
    public class MetadataProbe : DispatchProxy
    {
        /// <summary>Gets or sets the immutable metadata delegated by this custom implementation.</summary>
        internal IProperty Target { get; set; } = null!;

        /// <summary>Gets the number of actual property-member inspections.</summary>
        internal int PropertyReads { get; private set; }

        /// <inheritdoc />
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args
        )
        {
            var method = targetMethod ?? throw new InvalidOperationException("The proxy call requires metadata.");

            if (method.Name == "get_PropertyInfo")
            {
                PropertyReads++;
            }

            return method.Invoke(Target, BindingFlags.DoNotWrapExceptions, null, args, null);
        }
    }

    /// <summary>Changes assignability without changing the represented CLR type.</summary>
    private sealed class MutableDeclaringType : TypeDelegator
    {
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public MutableDeclaringType(
            Type type
        ) : base(type) { }

        /// <summary>Gets or sets whether the next alias check accepts the represented type.</summary>
        internal bool AcceptsAlias { get; set; } = true;

        /// <inheritdoc />
        public override bool IsAssignableFrom(
            Type? candidate
        ) => AcceptsAlias && base.IsAssignableFrom(candidate);
    }

    /// <summary>Preserves a real getter while deliberately exposing mutable reflection facts.</summary>
    private sealed class MutableProperty : PropertyInfo
    {
        private readonly PropertyInfo _property;
        private readonly Type _declaringType;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public MutableProperty(
            PropertyInfo property,
            Type declaringType
        )
        {
            _property = property;
            _declaringType = declaringType;
            ReportedName = _property.Name;
        }

        /// <summary>Gets or sets the name reported to EF's exact mapped-member lookup.</summary>
        internal string ReportedName { get; set; }

        /// <inheritdoc />
        public override PropertyAttributes Attributes => _property.Attributes;

        /// <inheritdoc />
        public override bool CanRead => _property.CanRead;

        /// <inheritdoc />
        public override bool CanWrite => _property.CanWrite;

        /// <inheritdoc />
        public override Type PropertyType => _property.PropertyType;

        /// <inheritdoc />
        public override Type DeclaringType => _declaringType;

        /// <inheritdoc />
        public override string Name => ReportedName;

        /// <inheritdoc />
        public override Type? ReflectedType => _property.ReflectedType;

        /// <inheritdoc />
        public override MethodInfo[] GetAccessors(
            bool nonPublic
        ) => _property.GetAccessors(nonPublic);

        /// <inheritdoc />
        public override MethodInfo? GetGetMethod(
            bool nonPublic
        ) => _property.GetGetMethod(nonPublic);

        /// <inheritdoc />
        public override MethodInfo? GetSetMethod(
            bool nonPublic
        ) => _property.GetSetMethod(nonPublic);

        /// <inheritdoc />
        public override ParameterInfo[] GetIndexParameters() => _property.GetIndexParameters();

        /// <inheritdoc />
        public override object? GetValue(
            object? obj,
            BindingFlags invokeAttr,
            Binder? binder,
            object?[]? index,
            CultureInfo? culture
        ) => _property.GetValue(obj, invokeAttr, binder, index, culture);

        /// <inheritdoc />
        public override void SetValue(
            object? obj,
            object? value,
            BindingFlags invokeAttr,
            Binder? binder,
            object?[]? index,
            CultureInfo? culture
        ) => _property.SetValue(obj, value, invokeAttr, binder, index, culture);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(
            bool inherit
        ) => _property.GetCustomAttributes(inherit);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(
            Type attributeType,
            bool inherit
        )
            => _property.GetCustomAttributes(attributeType, inherit);

        /// <inheritdoc />
        public override bool IsDefined(
            Type attributeType,
            bool inherit
        ) => _property.IsDefined(attributeType, inherit);
    }
}
