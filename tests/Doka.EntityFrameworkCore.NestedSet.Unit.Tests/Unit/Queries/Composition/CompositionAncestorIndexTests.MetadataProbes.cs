using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Arms only after public expression factories have completed their legitimate validation.</summary>
    private sealed class MetadataReadProbe
    {
        private bool _armed;

        /// <summary>Gets prohibited metadata callbacks after complete predicate construction.</summary>
        internal int Reads { get; private set; }

        /// <summary>Resets the observation and rejects all later metadata getter execution.</summary>
        internal void Arm()
        {
            Reads = 0;
            _armed = true;
        }

        /// <summary>Allows factory setup but rejects application metadata reads during classification.</summary>
        internal void Read()
        {
            if (!_armed)
            {
                return;
            }

            Reads++;

            throw new InvalidOperationException("Classification invoked an application-controlled metadata getter.");
        }
    }

    /// <summary>Preserves a valid constructor while making its declaring-type getter application-controlled.</summary>
    private sealed class ConstructorMetadataProbe(
        ConstructorInfo constructor,
        MetadataReadProbe probe
    ) : ConstructorInfo
    {
        /// <inheritdoc />
        public override Type? DeclaringType
        {
            get
            {
                probe.Read();

                return constructor.DeclaringType;
            }
        }

        /// <inheritdoc />
        public override MethodAttributes Attributes => constructor.Attributes;

        /// <inheritdoc />
        public override RuntimeMethodHandle MethodHandle => constructor.MethodHandle;

        /// <inheritdoc />
        public override string Name => constructor.Name;

        /// <inheritdoc />
        public override Type? ReflectedType => constructor.ReflectedType;

        /// <inheritdoc />
        public override MethodImplAttributes GetMethodImplementationFlags()
            => constructor.GetMethodImplementationFlags();

        /// <inheritdoc />
        public override ParameterInfo[] GetParameters() => constructor.GetParameters();

        /// <inheritdoc />
        public override object[] GetCustomAttributes(bool inherit) => constructor.GetCustomAttributes(inherit);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(
            Type attributeType,
            bool inherit
        ) => constructor.GetCustomAttributes(attributeType, inherit);

        /// <inheritdoc />
        public override bool IsDefined(
            Type attributeType,
            bool inherit
        ) => constructor.IsDefined(attributeType, inherit);

        /// <inheritdoc />
        public override object Invoke(
            BindingFlags invokeAttr,
            Binder? binder,
            object?[]? parameters,
            CultureInfo? culture
        ) => throw new InvalidOperationException("A source classifier must not invoke a constructor.");

        /// <inheritdoc />
        public override object? Invoke(
            object? obj,
            BindingFlags invokeAttr,
            Binder? binder,
            object?[]? parameters,
            CultureInfo? culture
        ) => throw new InvalidOperationException("A source classifier must not invoke a constructor.");
    }

    /// <summary>Preserves List's ordinary indexer while controlling its declared property type.</summary>
    private sealed class IndexMetadataProbe(
        PropertyInfo property,
        MetadataReadProbe probe
    ) : PropertyInfo
    {
        /// <inheritdoc />
        public override Type PropertyType
        {
            get
            {
                probe.Read();

                return property.PropertyType;
            }
        }

        /// <inheritdoc />
        public override PropertyAttributes Attributes => property.Attributes;

        /// <inheritdoc />
        public override bool CanRead => property.CanRead;

        /// <inheritdoc />
        public override bool CanWrite => property.CanWrite;

        /// <inheritdoc />
        public override Type? DeclaringType => property.DeclaringType;

        /// <inheritdoc />
        public override string Name => property.Name;

        /// <inheritdoc />
        public override Type? ReflectedType => property.ReflectedType;

        /// <inheritdoc />
        public override MethodInfo[] GetAccessors(bool nonPublic) => property.GetAccessors(nonPublic);

        /// <inheritdoc />
        public override MethodInfo? GetGetMethod(bool nonPublic) => property.GetGetMethod(nonPublic);

        /// <inheritdoc />
        public override MethodInfo? GetSetMethod(bool nonPublic) => property.GetSetMethod(nonPublic);

        /// <inheritdoc />
        public override ParameterInfo[] GetIndexParameters() => property.GetIndexParameters();

        /// <inheritdoc />
        public override object[] GetCustomAttributes(bool inherit) => property.GetCustomAttributes(inherit);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(
            Type attributeType,
            bool inherit
        ) => property.GetCustomAttributes(attributeType, inherit);

        /// <inheritdoc />
        public override bool IsDefined(
            Type attributeType,
            bool inherit
        ) => property.IsDefined(attributeType, inherit);

        /// <inheritdoc />
        public override object? GetValue(
            object? obj,
            BindingFlags invokeAttr,
            Binder? binder,
            object?[]? index,
            CultureInfo? culture
        ) => throw new InvalidOperationException("A source classifier must not invoke an indexer.");

        /// <inheritdoc />
        public override void SetValue(
            object? obj,
            object? value,
            BindingFlags invokeAttr,
            Binder? binder,
            object?[]? index,
            CultureInfo? culture
        ) => throw new InvalidOperationException("A source classifier must not modify an indexer.");
    }

    /// <summary>Supplies a valid declared null type without allowing later virtual type inspection.</summary>
    private sealed class ConstantMetadataProbe(MetadataReadProbe probe) : TypeDelegator(typeof(string))
    {
        /// <inheritdoc />
        public override Type UnderlyingSystemType
        {
            get
            {
                probe.Read();

                return base.UnderlyingSystemType;
            }
        }
    }

    /// <summary>Detects dynamic reduction reaching CallSite.Create's virtual delegate-type validation.</summary>
    private sealed class DynamicDelegateMetadataProbe(MetadataReadProbe probe)
        : TypeDelegator(typeof(Func<CallSite, object, object>))
    {
        /// <inheritdoc />
        public override bool IsSubclassOf(Type type)
        {
            probe.Read();

            return base.IsSubclassOf(type);
        }
    }

    /// <summary>Provides an inert binder; these tests prove type-getter safety, not binder invocation.</summary>
    private sealed class MetadataBinderProbe : CallSiteBinder
    {
        /// <inheritdoc />
        public override Expression Bind(
            object[] args,
            ReadOnlyCollection<ParameterExpression> parameters,
            LabelTarget returnLabel
        ) => throw new InvalidOperationException("A source classifier must not bind a dynamic call site.");
    }
}
