using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TypedEqualityConstructionTests
{
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
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
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
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
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

    /// <summary>Throws if expression construction accidentally executes the discovered CLR operator.</summary>
    private readonly struct OperatorProbe
    {
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public OperatorProbe(
            int value
        )
        {
            Value = value;
        }

        /// <summary>Gets the probe value used only by ordinary object equality.</summary>
        private int Value { get; }

        /// <summary>Rejects executing operator equality while its expression node is being built.</summary>
        public static bool operator==(
            OperatorProbe left,
            OperatorProbe right
        )
        {
            throw new InvalidOperationException("Construction must not invoke CLR equality.");
        }

        /// <summary>Rejects executing operator inequality while its expression node is being built.</summary>
        public static bool operator!=(
            OperatorProbe left,
            OperatorProbe right
        )
        {
            throw new InvalidOperationException("Construction must not invoke CLR inequality.");
        }

        /// <inheritdoc />
        public override bool Equals(
            object? obj
        ) => obj is OperatorProbe other && Value == other.Value;

        /// <inheritdoc />
        public override int GetHashCode() => Value;
    }
}
