namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Preserves scalar parameter types for native and operator-free converted value equality.</summary>
/// <typeparam name="TValue">The exact expression type, including nullable metadata types.</typeparam>
internal static class NestedSetTypedEquality<TValue>
{
    private static readonly Func<Expression, Expression, Expression> s_equal = CreateEquality();

    /// <summary>Builds native equality without requiring a converted struct to declare an equality operator.</summary>
    internal static Expression Equal(
        Expression left,
        Expression right
    ) => s_equal(left, right);

    /// <summary>Resolves the supported expression shape once for each closed mapped role type.</summary>
    private static Func<Expression, Expression, Expression> CreateEquality()
    {
        var value = Expression.Parameter(typeof(TValue), "value");

        try
        {
            var equality = Expression.Equal(value, value);

            if (equality.Method is { } method)
            {
                // WHY: The probe already resolved the operator. Retain that exact method so repeated builds
                // preserve native/lifted equality without rediscovering operators through reflection.
                return (left,
                    right) => Expression.Equal(left, right, liftToNull: false, method);
            }

            return static (left,
                right) => Expression.Equal(left, right);
        }
        catch (InvalidOperationException)
        {
            // WHY: Nullable operator-free structs expose Equals(object), not Equals(TValue). A relaxed
            // reflection lookup can return that overload and produce an invalid unboxed call expression.
            // Only an exact typed overload belongs here; the explicit object marker below handles the rest.
            var equals = typeof(TValue).GetMethod(
                nameof(object.Equals),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.ExactBinding,
                binder: null,
                types: [typeof(TValue)],
                modifiers: null);

            if (equals is not null)
            {
                // WHY: EF translates concrete Equals(TValue) to database equality for converted structs even
                // when their CLR type implements IEquatable<TValue> without defining operator ==.
                return (left,
                    right) => Expression.Call(left, equals, right);
            }

            // WHY: An operator-free struct can expose only Equals(object). This erasure exists solely inside the
            // provider-translated expression; no runtime key, scope or parent is transported as object.
            var objectEquals = typeof(object).GetMethod(
                nameof(object.Equals),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.ExactBinding,
                binder: null,
                types: [typeof(object), typeof(object)],
                modifiers: null)!;

            // WHY: The exact framework overload is stable and already known; name-based Expression.Call would
            // repeat reflection and overload discovery for every equality node.
            return (left,
                right) => Expression.Call(
                objectEquals,
                Expression.Convert(left, typeof(object)),
                Expression.Convert(right, typeof(object)));
        }
    }
}
