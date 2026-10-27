using System.Globalization;
using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves native and converted equality while binding reflection once per mapped type.</summary>
/// <param name="output">Records paired construction allocation evidence.</param>
[Collection("Allocation measurements")]
public sealed class TypedEqualityConstructionTests(ITestOutputHelper output)
{
    /// <summary>Cached operator metadata retains the framework's native and nullable binary expression shape.</summary>
    /// <param name="shape">The native or operator-free mapped expression type.</param>
    [Theory]
    [InlineData("Guid")]
    [InlineData("NullableGuid")]
    [InlineData("String")]
    [InlineData("Int")]
    [InlineData("NullableInt")]
    [InlineData("Long")]
    [InlineData("NullableLong")]
    [InlineData("Typed")]
    [InlineData("NullableTyped")]
    [InlineData("Object")]
    [InlineData("NullableObject")]
    public void ConstructionRetainsOriginalExpressionShape(
        string shape
    )
    {
        // Arrange
        var prepared = Shape(shape);
        var expected = prepared.Reference();

        // Act
        var actual = prepared.Current();

        // Assert
        Assert.Equal(expected.NodeType, actual.NodeType);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.ToString(), actual.ToString());

        if (expected is BinaryExpression expectedBinary)
        {
            var binary = Assert.IsAssignableFrom<BinaryExpression>(actual);
            Assert.Equal(expectedBinary.Method, binary.Method);
            Assert.Equal(expectedBinary.IsLifted, binary.IsLifted);
            Assert.Equal(expectedBinary.IsLiftedToNull, binary.IsLiftedToNull);
            Assert.Same(expectedBinary.Left, binary.Left);
            Assert.Same(expectedBinary.Right, binary.Right);
        }
        else
        {
            var call = Assert.IsAssignableFrom<MethodCallExpression>(actual);
            Assert.Equal(Assert.IsAssignableFrom<MethodCallExpression>(expected).Method, call.Method);
        }
    }

    /// <summary>Nullable and converted equality retain positive, negative and null results.</summary>
    /// <param name="shape">The nullable or reference expression type.</param>
    /// <param name="left">The null, first, different, or provider-equivalent value selector.</param>
    /// <param name="right">The independently selected right operand.</param>
    [Theory]
    [InlineData("NullableGuid", 0, 0)]
    [InlineData("NullableGuid", 0, 1)]
    [InlineData("NullableGuid", 1, 0)]
    [InlineData("NullableGuid", 1, 1)]
    [InlineData("NullableGuid", 1, 2)]
    [InlineData("NullableInt", 0, 0)]
    [InlineData("NullableInt", 0, 1)]
    [InlineData("NullableInt", 1, 0)]
    [InlineData("NullableInt", 1, 1)]
    [InlineData("NullableInt", 1, 2)]
    [InlineData("String", 0, 0)]
    [InlineData("String", 0, 1)]
    [InlineData("String", 1, 0)]
    [InlineData("String", 1, 1)]
    [InlineData("String", 1, 2)]
    [InlineData("NullableTyped", 0, 0)]
    [InlineData("NullableTyped", 0, 1)]
    [InlineData("NullableTyped", 1, 0)]
    [InlineData("NullableTyped", 1, 1)]
    [InlineData("NullableTyped", 1, 2)]
    [InlineData("NullableTyped", 1, 3)]
    [InlineData("NullableObject", 0, 0)]
    [InlineData("NullableObject", 0, 1)]
    [InlineData("NullableObject", 1, 0)]
    [InlineData("NullableObject", 1, 1)]
    [InlineData("NullableObject", 1, 2)]
    [InlineData("NullableObject", 1, 3)]
    public void EqualityRetainsNullAndValueSemantics(
        string shape,
        int left,
        int right
    )
    {
        // Arrange
        var prepared = Values(shape, left, right);
        var expected = Expression.Lambda<Func<bool>>(prepared.Reference()).Compile()();

        // Act
        var actual = Expression.Lambda<Func<bool>>(prepared.Current()).Compile()();

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Building native or lifted operator nodes does not execute the application's CLR operator.</summary>
    /// <param name="nullable">Whether the discovered operator is used through nullable lifting.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatorDiscoveryDoesNotInvokeClrOperator(
        bool nullable
    )
    {
        // Arrange
        var prepared = nullable ? Native<OperatorProbe?>() : Native<OperatorProbe>();

        // Act
        var actual = prepared.Current();

        // Assert
        var binary = Assert.IsAssignableFrom<BinaryExpression>(actual);
        Assert.Equal("op_Equality", binary.Method?.Name);
        Assert.Equal(nullable, binary.IsLifted);
        Assert.False(binary.IsLiftedToNull);
    }

    /// <summary>Retained methods preserve measured savings and do not penalize unchanged native factories.</summary>
    /// <param name="shape">The native, lifted or operator-free construction path.</param>
    /// <param name="minimumSavedBytes">The conservative required saving for optimized metadata paths.</param>
    [Theory]
    [InlineData("Guid", 32)]
    [InlineData("NullableGuid", 32)]
    [InlineData("String", 32)]
    [InlineData("Int", 0)]
    [InlineData("NullableInt", 0)]
    [InlineData("Typed", 0)]
    [InlineData("NullableTyped", 32)]
    [InlineData("Object", 32)]
    [InlineData("NullableObject", 32)]
    public void ConstructionAllocationDoesNotRegress(
        string shape,
        int minimumSavedBytes
    )
    {
        // Arrange
        var prepared = Shape(shape);

        for (var index = 0; index < 1000; index++)
        {
            GC.KeepAlive(prepared.Reference());
            GC.KeepAlive(prepared.Current());
        }

        // Act
        var allocated = QueryConstructionAllocation.MeasurePaired(prepared.Reference, prepared.Current);

        // Assert
        output.WriteLine($"Equality shape={shape}; reference={allocated.Baseline} B/op; "
            + $"cached={allocated.Expanded} B/op.");

        // WHY: Native nodes remain necessary. Optimized cases must also reject restoration of operator/overload
        // discovery, with a conservative saving below the observed 144-448 bytes rather than exact runtime sizes.
        Assert.InRange(allocated.Expanded, 0, allocated.Baseline - minimumSavedBytes + 8);
    }

    /// <summary>Prepares framework-reference factories for native and explicitly operator-free shapes.</summary>
    private static ConstructionCase Shape(
        string shape
    ) => shape switch
    {
        "Guid" => Native<Guid>(),
        "NullableGuid" => Native<Guid?>(),
        "String" => Native<string>(),
        "Int" => Native<int>(),
        "NullableInt" => Native<int?>(),
        "Long" => Native<long>(),
        "NullableLong" => Native<long?>(),
        "Typed" => OperatorFree<TypedIdentity>(),
        "NullableTyped" => OperatorFree<TypedIdentity?>(),
        "Object" => OperatorFree<ObjectIdentity>(),
        "NullableObject" => OperatorFree<ObjectIdentity?>(),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Uses the framework's normal operator resolution as the unchanged semantic reference.</summary>
    private static ConstructionCase Native<T>()
    {
        var left = Expression.Parameter(typeof(T), "left");
        var right = Expression.Parameter(typeof(T), "right");

        return new ConstructionCase(
            () => Expression.Equal(left, right),
            () => NestedSetTypedEquality<T>.Equal(left, right));
    }

    /// <summary>Retains the old exact typed-Equals or name-based object-Equals reference factory.</summary>
    private static ConstructionCase OperatorFree<T>()
    {
        var left = Expression.Parameter(typeof(T), "left");
        var right = Expression.Parameter(typeof(T), "right");

        return OperatorFree<T>(left, right);
    }

    /// <summary>Creates operator-free reference expressions for prepared parameters or nullable constants.</summary>
    private static ConstructionCase OperatorFree<T>(
        Expression left,
        Expression right
    )
    {
        var equals = typeof(T).GetMethod(nameof(object.Equals),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.ExactBinding,
            binder: null, types: [typeof(T)], modifiers: null);

        Func<Expression> reference = equals is null
            ? () => Expression.Call(typeof(object), nameof(object.Equals), [],
                Expression.Convert(left, typeof(object)), Expression.Convert(right, typeof(object)))
            : () => Expression.Call(left, equals, right);

        return new ConstructionCase(reference, () => NestedSetTypedEquality<T>.Equal(left, right));
    }

    /// <summary>Prepares constants without confusing nullable boxing with the declared expression type.</summary>
    private static ConstructionCase Values(
        string shape,
        int left,
        int right
    ) => shape switch
    {
        "NullableGuid" => NativeValues<Guid?>(GuidValue(left), GuidValue(right)),
        "NullableInt" => NativeValues<int?>(left == 0 ? null : left, right == 0 ? null : right),
        "String" => NativeValues<string?>(
            left == 0 ? null : left.ToString(CultureInfo.InvariantCulture),
            right == 0 ? null : right.ToString(CultureInfo.InvariantCulture)),
        "NullableTyped" => OperatorFreeValues<TypedIdentity?>(
            left == 0 ? null : new TypedIdentity(IdentityValue(left)),
            right == 0 ? null : new TypedIdentity(IdentityValue(right))),
        "NullableObject" => OperatorFreeValues<ObjectIdentity?>(
            left == 0 ? null : new ObjectIdentity(IdentityValue(left)),
            right == 0 ? null : new ObjectIdentity(IdentityValue(right))),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Builds native equality from typed constants for null/value execution controls.</summary>
    private static ConstructionCase NativeValues<T>(
        T leftValue,
        T rightValue
    )
    {
        var left = Expression.Constant(leftValue, typeof(T));
        var right = Expression.Constant(rightValue, typeof(T));

        return new ConstructionCase(
            () => Expression.Equal(left, right),
            () => NestedSetTypedEquality<T>.Equal(left, right));
    }

    /// <summary>Retains operator-free fallback semantics for nullable boxed constants.</summary>
    private static ConstructionCase OperatorFreeValues<T>(
        T leftValue,
        T rightValue
    ) => OperatorFree<T>(Expression.Constant(leftValue, typeof(T)), Expression.Constant(rightValue, typeof(T)));

    /// <summary>Selects stable Guid values while retaining an explicit null case.</summary>
    private static Guid? GuidValue(
        int kind
    ) => kind == 0 ? null : new Guid(kind, 0, 0, new byte[8]);

    /// <summary>Includes an unequal model value sharing the custom identity's normalized equality value.</summary>
    private static int IdentityValue(
        int kind
    ) => kind switch { 1 => 17, 2 => 18, 3 => 27, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    /// <summary>Keeps both prepared construction delegates outside measured operations.</summary>
    /// <param name="Reference">The original framework-backed construction factory.</param>
    /// <param name="Current">The current cached construction factory.</param>
    private sealed record ConstructionCase(Func<Expression> Reference, Func<Expression> Current);

    /// <summary>Provides exact typed equality without declaring an equality operator.</summary>
    private readonly struct TypedIdentity(int value) : IEquatable<TypedIdentity>
    {
        /// <summary>Gets the model identity before its normalized equality comparison.</summary>
        private int Value { get; } = value;

        /// <inheritdoc />
        public bool Equals(TypedIdentity other) => Value % 10 == other.Value % 10;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is TypedIdentity other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Value % 10;
    }

    /// <summary>Provides only object equality for the explicit object marker fallback.</summary>
    private readonly struct ObjectIdentity(int value)
    {
        /// <summary>Gets the model identity before its normalized equality comparison.</summary>
        private int Value { get; } = value;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is ObjectIdentity other && Value % 10 == other.Value % 10;

        /// <inheritdoc />
        public override int GetHashCode() => Value % 10;
    }

    /// <summary>Throws if expression construction accidentally executes the discovered CLR operator.</summary>
    private readonly struct OperatorProbe(int value)
    {
        /// <summary>Gets the probe value used only by ordinary object equality.</summary>
        private int Value { get; } = value;

        /// <summary>Rejects executing operator equality while its expression node is being built.</summary>
        public static bool operator ==(OperatorProbe left, OperatorProbe right)
            => throw new InvalidOperationException("Construction must not invoke CLR equality.");

        /// <summary>Rejects executing operator inequality while its expression node is being built.</summary>
        public static bool operator !=(OperatorProbe left, OperatorProbe right)
            => throw new InvalidOperationException("Construction must not invoke CLR inequality.");

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is OperatorProbe other && Value == other.Value;

        /// <inheritdoc />
        public override int GetHashCode() => Value;
    }
}
