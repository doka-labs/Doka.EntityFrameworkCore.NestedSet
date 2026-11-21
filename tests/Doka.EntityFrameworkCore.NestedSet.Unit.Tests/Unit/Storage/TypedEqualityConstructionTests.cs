using System.Globalization;
using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves native and converted equality while binding reflection once per mapped type.</summary>
public sealed partial class TypedEqualityConstructionTests
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
    public void Current_Construction_RetainsOriginalExpressionShape(
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
            Assert.Equal(
                Assert.IsAssignableFrom<MethodCallExpression>(expected)
                    .Method,
                call.Method);
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
    public void Compile_EqualityPredicate_RetainsNullAndValueSemantics(
        string shape,
        int left,
        int right
    )
    {
        // Arrange
        var prepared = Values(shape, left, right);
        var expected = Expression
            .Lambda<Func<bool>>(prepared.Reference())
            .Compile()();

        // Act
        var actual = Expression
            .Lambda<Func<bool>>(prepared.Current())
            .Compile()();

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Building native or lifted operator nodes does not execute the application's CLR operator.</summary>
    /// <param name="nullable">Whether the discovered operator is used through nullable lifting.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Current_OperatorDiscovery_DoesNotInvokeClrOperator(
        bool nullable
    )
    {
        // Arrange
        var prepared = nullable
            ? Native<OperatorProbe?>()
            : Native<OperatorProbe>();

        // Act
        var actual = prepared.Current();

        // Assert
        var binary = Assert.IsAssignableFrom<BinaryExpression>(actual);
        Assert.Equal("op_Equality", binary.Method?.Name);
        Assert.Equal(nullable, binary.IsLifted);
        Assert.False(binary.IsLiftedToNull);
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
        var equals = typeof(T).GetMethod(
            nameof(object.Equals),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.ExactBinding,
            binder: null,
            types: [typeof(T)],
            modifiers: null);

        Func<Expression> reference = equals is null
            ? () => Expression.Call(
                typeof(object),
                nameof(object.Equals),
                [],
                Expression.Convert(left, typeof(object)),
                Expression.Convert(right, typeof(object)))
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
        "NullableGuid" => NativeValues(GuidValue(left), GuidValue(right)),
        "NullableInt" => NativeValues<int?>(
            left == 0
                ? null
                : left,
            right == 0
                ? null
                : right),
        "String" => NativeValues<string?>(
            left == 0
                ? null
                : left.ToString(CultureInfo.InvariantCulture),
            right == 0
                ? null
                : right.ToString(CultureInfo.InvariantCulture)),
        "NullableTyped" => OperatorFreeValues<TypedIdentity?>(
            left == 0
                ? null
                : new TypedIdentity(IdentityValue(left)),
            right == 0
                ? null
                : new TypedIdentity(IdentityValue(right))),
        "NullableObject" => OperatorFreeValues<ObjectIdentity?>(
            left == 0
                ? null
                : new ObjectIdentity(IdentityValue(left)),
            right == 0
                ? null
                : new ObjectIdentity(IdentityValue(right))),
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
    ) => kind == 0
        ? null
        : new Guid(kind, 0, 0, new byte[8]);

    /// <summary>Includes an unequal model value sharing the custom identity's normalized equality value.</summary>
    private static int IdentityValue(
        int kind
    ) => kind switch { 1 => 17, 2 => 18, 3 => 27, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
}
