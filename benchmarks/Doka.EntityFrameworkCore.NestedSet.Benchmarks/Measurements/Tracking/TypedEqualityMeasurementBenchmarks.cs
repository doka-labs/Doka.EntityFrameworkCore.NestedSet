using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Observes cached and framework-reference equality construction with native, lifted and operator-free keys.
/// </summary>
public class TypedEqualityMeasurementBenchmarks
{
    private ConstructionCase _case = null!;

    /// <summary>Gets or sets the exact expression-key representation.</summary>
    [Params("Guid", "NullableGuid", "String", "Int", "NullableInt", "Long", "NullableLong",
        "Typed", "NullableTyped", "Object", "NullableObject")]
    public string ShapeName { get; set; } = "Guid";

    /// <summary>Prepares both reference and production expression factories before native measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _case = Shape(ShapeName);
        VerifyWorkload();
    }

    /// <summary>Builds the independent framework/reference equality expression.</summary>
    /// <returns>The completed reference expression.</returns>
    [Benchmark(Baseline = true)]
    public Expression Reference() => _case.Reference();

    /// <summary>Builds the actual cached production equality expression.</summary>
    /// <returns>The completed production expression.</returns>
    [Benchmark]
    public Expression Current() => _case.Current();

    /// <summary>Requires equivalent expression shape, type and operands independently of measured bytes.</summary>
    [GlobalCleanup]
    public void VerifyWorkload()
    {
        var expected = Reference();
        var actual = Current();
        if (expected.NodeType != actual.NodeType
            || expected.Type != actual.Type
            || expected.ToString() != actual.ToString())
        {
            throw new InvalidOperationException("The equality construction workload changed expression semantics.");
        }
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


    /// <summary>Keeps both prepared construction delegates outside measured operations.</summary>
    /// <param name="Reference">The original framework-backed construction factory.</param>
    /// <param name="Current">The current cached construction factory.</param>
    private sealed record ConstructionCase(
        Func<Expression> Reference,
        Func<Expression> Current
    );

    /// <summary>Provides exact typed equality without declaring an equality operator.</summary>
    private readonly struct TypedIdentity : IEquatable<TypedIdentity>
    {
        /// <summary>Initializes the benchmark fixture with its explicit dependencies.</summary>
        public TypedIdentity(
            int value
        )
        {
            Value = value;
        }

        /// <summary>Gets the model identity before its normalized equality comparison.</summary>
        private int Value { get; }

        /// <inheritdoc />
        public bool Equals(
            TypedIdentity other
        ) => Value % 10 == other.Value % 10;

        /// <inheritdoc />
        public override bool Equals(
            object? obj
        ) => obj is TypedIdentity other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Value % 10;
    }

    /// <summary>Provides only object equality for the explicit object marker fallback.</summary>
    private readonly struct ObjectIdentity
    {
        /// <summary>Initializes the benchmark fixture with its explicit dependencies.</summary>
        public ObjectIdentity(
            int value
        )
        {
            Value = value;
        }

        /// <summary>Gets the model identity before its normalized equality comparison.</summary>
        private int Value { get; }

        /// <inheritdoc />
        public override bool Equals(
            object? obj
        ) => obj is ObjectIdentity other && Value % 10 == other.Value % 10;

        /// <inheritdoc />
        public override int GetHashCode() => Value % 10;
    }


}
