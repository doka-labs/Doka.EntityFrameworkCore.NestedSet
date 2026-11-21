namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetComplexEntryAccess
{
    private sealed partial class Bindings
    {
        /// <summary>Closes the exact native Ordinal operation before the recovery seam is published.</summary>
        private static Func<object, bool, int> BindOrdinal(
            Type nativeComplex,
            ParameterExpression complex,
            ParameterExpression original,
            UnaryExpression nativeElement
        )
        {
            var readOrdinal = RequireMethod(nativeComplex, "get_Ordinal", typeof(int), []);
            var readOriginalOrdinal = RequireMethod(nativeComplex, "get_OriginalOrdinal", typeof(int), []);
            return Expression
                .Lambda<Func<object, bool, int>>(
                    Expression.Condition(
                        original,
                        Expression.Call(nativeElement, readOriginalOrdinal),
                        Expression.Call(nativeElement, readOrdinal)),
                    complex,
                    original)
                .Compile();
        }

        /// <summary>Closes the exact native Ordinals operation before the recovery seam is published.</summary>
        private static Func<object, IReadOnlyList<int>> BindOrdinals(
            Type nativeBase,
            ParameterExpression owner,
            UnaryExpression nativeOwner
        )
        {
            return Expression
                .Lambda<Func<object, IReadOnlyList<int>>>(
                    Expression.Call(
                        nativeOwner,
                        RequireMethod(nativeBase, "GetOrdinals", typeof(IReadOnlyList<int>), [])),
                    owner)
                .Compile();
        }

        /// <summary>Closes the exact native SetOrdinal operation before the recovery seam is published.</summary>
        private static Action<object, int, bool> BindSetOrdinal(
            Type nativeComplex,
            ParameterExpression complex,
            ParameterExpression original,
            ParameterExpression ordinal,
            UnaryExpression nativeElement
        )
        {
            return Expression
                .Lambda<Action<object, int, bool>>(
                    Expression.IfThenElse(
                        original,
                        Expression.Call(
                            nativeElement,
                            RequireMethod(nativeComplex, "set_OriginalOrdinal", typeof(void), [typeof(int)]),
                            ordinal),
                        Expression.Call(
                            nativeElement,
                            RequireMethod(nativeComplex, "set_Ordinal", typeof(void), [typeof(int)]),
                            ordinal)),
                    complex,
                    ordinal,
                    original)
                .Compile();
        }

        /// <summary>Closes the exact native State operation before the recovery seam is published.</summary>
        private static Func<object, EntityState> BindState(
            Type nativeBase,
            ParameterExpression owner,
            UnaryExpression nativeOwner
        )
        {
            return Expression
                .Lambda<Func<object, EntityState>>(
                    Expression.Call(nativeOwner, RequireMethod(nativeBase, "get_EntityState", typeof(EntityState), [])),
                    owner)
                .Compile();
        }

        /// <summary>Closes the exact native SetState operation before the recovery seam is published.</summary>
        private static Action<object, EntityState> BindSetState(
            Type nativeBase,
            ParameterExpression owner,
            ParameterExpression state,
            UnaryExpression nativeOwner
        )
        {
            // WHY: Public element.State defaults modifyProperties to true. Topology recovery restores flags
            // separately, so retain EF transitions without acceptance or blanket Modified marking.
            return Expression
                .Lambda<Action<object, EntityState>>(
                    Expression.Call(
                        nativeOwner,
                        RequireMethod(
                            nativeBase,
                            "SetEntityState",
                            typeof(void),
                            [
                                typeof(EntityState),
                                typeof(bool),
                                typeof(bool),
                                typeof(EntityState?),
                                typeof(EntityState?),
                            ]),
                        state,
                        Expression.Constant(false),
                        Expression.Constant(false),
                        Expression.Constant(null, typeof(EntityState?)),
                        Expression.Constant(null, typeof(EntityState?))),
                    owner,
                    state)
                .Compile();
        }

        /// <summary>Closes the exact native Capacity operation before the recovery seam is published.</summary>
        private static Action<object, IComplexProperty, int, int> BindCapacity(
            Type nativeBase,
            ParameterExpression owner,
            ParameterExpression property,
            ParameterExpression currentCount,
            ParameterExpression originalCount,
            UnaryExpression nativeOwner
        )
        {
            return Expression
                .Lambda<Action<object, IComplexProperty, int, int>>(
                    Expression.Call(
                        nativeOwner,
                        RequireMethod(
                            nativeBase,
                            "EnsureComplexCollectionEntriesCapacity",
                            typeof(void),
                            [typeof(IComplexProperty), typeof(int), typeof(int), typeof(bool)]),
                        property,
                        currentCount,
                        originalCount,
                        Expression.Constant(true)),
                    owner,
                    property,
                    currentCount,
                    originalCount)
                .Compile();
        }

        /// <summary>Closes the exact native Original operation before the recovery seam is published.</summary>
        private static Func<object, IComplexProperty, object?> BindOriginal(
            Type nativeBase,
            ParameterExpression owner,
            ParameterExpression property,
            UnaryExpression nativeOwner
        )
        {
            return Expression
                .Lambda<Func<object, IComplexProperty, object?>>(
                    Expression.Call(
                        nativeOwner,
                        RequireMethod(nativeBase, "GetOriginalValue", typeof(object), [typeof(IPropertyBase)]),
                        property),
                    owner,
                    property)
                .Compile();
        }

        /// <summary>Closes the exact native SetModified operation before the recovery seam is published.</summary>
        private static Action<object, IComplexProperty, bool> BindSetModified(
            Type nativeBase,
            ParameterExpression owner,
            ParameterExpression property,
            ParameterExpression modified,
            UnaryExpression nativeOwner
        )
        {
            // WHY: Recursive public collection flag setters can destroy legitimate Added or deleted elements.
            // Restore only this aggregate flag; the journal restores each captured element's state independently.
            return Expression
                .Lambda<Action<object, IComplexProperty, bool>>(
                    Expression.Call(
                        nativeOwner,
                        RequireMethod(
                            nativeBase,
                            "SetPropertyModified",
                            typeof(void),
                            [typeof(IComplexProperty), typeof(bool), typeof(bool)]),
                        property,
                        modified,
                        Expression.Constant(false)),
                    owner,
                    property,
                    modified)
                .Compile();
        }
    }
}
