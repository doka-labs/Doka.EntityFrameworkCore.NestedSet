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
    private sealed class ConstructorMetadataProbe : ConstructorInfo
    {
        private readonly ConstructorInfo _constructor;
        private readonly MetadataReadProbe _probe;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ConstructorMetadataProbe(
            ConstructorInfo constructor,
            MetadataReadProbe probe
        )
        {
            _constructor = constructor;
            _probe = probe;
        }

        /// <inheritdoc />
        public override Type? DeclaringType
        {
            get
            {
                _probe.Read();

                return _constructor.DeclaringType;
            }
        }

        /// <inheritdoc />
        public override MethodAttributes Attributes => _constructor.Attributes;

        /// <inheritdoc />
        public override RuntimeMethodHandle MethodHandle => _constructor.MethodHandle;

        /// <inheritdoc />
        public override string Name => _constructor.Name;

        /// <inheritdoc />
        public override Type? ReflectedType => _constructor.ReflectedType;

        /// <inheritdoc />
        public override MethodImplAttributes GetMethodImplementationFlags()
            => _constructor.GetMethodImplementationFlags();

        /// <inheritdoc />
        public override ParameterInfo[] GetParameters() => _constructor.GetParameters();

        /// <inheritdoc />
        public override object[] GetCustomAttributes(
            bool inherit
        ) => _constructor.GetCustomAttributes(inherit);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(
            Type attributeType,
            bool inherit
        ) => _constructor.GetCustomAttributes(attributeType, inherit);

        /// <inheritdoc />
        public override bool IsDefined(
            Type attributeType,
            bool inherit
        ) => _constructor.IsDefined(attributeType, inherit);

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
    private sealed class IndexMetadataProbe : PropertyInfo
    {
        private readonly PropertyInfo _property;
        private readonly MetadataReadProbe _probe;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public IndexMetadataProbe(
            PropertyInfo property,
            MetadataReadProbe probe
        )
        {
            _property = property;
            _probe = probe;
        }

        /// <inheritdoc />
        public override Type PropertyType
        {
            get
            {
                _probe.Read();

                return _property.PropertyType;
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
        public override object[] GetCustomAttributes(
            bool inherit
        ) => _property.GetCustomAttributes(inherit);

        /// <inheritdoc />
        public override object[] GetCustomAttributes(
            Type attributeType,
            bool inherit
        ) => _property.GetCustomAttributes(attributeType, inherit);

        /// <inheritdoc />
        public override bool IsDefined(
            Type attributeType,
            bool inherit
        ) => _property.IsDefined(attributeType, inherit);

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
    private sealed class ConstantMetadataProbe : TypeDelegator
    {
        private readonly MetadataReadProbe _probe;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ConstantMetadataProbe(
            MetadataReadProbe probe
        ) : base(typeof(string))
        {
            _probe = probe;
        }

        /// <inheritdoc />
        public override Type UnderlyingSystemType
        {
            get
            {
                _probe.Read();

                return base.UnderlyingSystemType;
            }
        }
    }

    /// <summary>Detects dynamic reduction reaching CallSite.Create's virtual delegate-type validation.</summary>
    private sealed class DynamicDelegateMetadataProbe
        : TypeDelegator
    {
        private readonly MetadataReadProbe _probe;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public DynamicDelegateMetadataProbe(
            MetadataReadProbe probe
        ) : base(typeof(Func<CallSite, object, object>))
        {
            _probe = probe;
        }

        /// <inheritdoc />
        public override bool IsSubclassOf(
            Type type
        )
        {
            _probe.Read();

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
