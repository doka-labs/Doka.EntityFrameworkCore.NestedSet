namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetComplexEntryAccess
{
    private sealed partial class Bindings
    {
        /// <summary>Closes the exact native Setter operation before the recovery seam is published.</summary>
        private static Func<IPropertyBase, IClrPropertySetter> BindSetter(
            Type runtimeProperty,
            ParameterExpression metadata
        )
        {
            return Expression
                .Lambda<Func<IPropertyBase, IClrPropertySetter>>(
                    Expression.Call(
                        Expression.Convert(metadata, runtimeProperty),
                        RequireMethod(runtimeProperty, "GetSetter", typeof(IClrPropertySetter), [])),
                    metadata)
                .Compile();
        }

        /// <summary>Closes the exact native CreateCollection operation before the recovery seam is published.</summary>
        private static Func<IComplexProperty, int, IList> BindCreateCollection(
            Type runtimeProperty,
            ParameterExpression property,
            ParameterExpression currentCount
        )
        {
            // WHY: Retained ordinals can outlive the current CLR list. Ask EF for correctly typed staging
            // storage instead of assuming List<T>, invoking arbitrary constructors or copying caller storage.
            return Expression
                .Lambda<Func<IComplexProperty, int, IList>>(
                    Expression.Convert(
                        Expression.Call(
                            Expression.Call(
                                Expression.Convert(property, runtimeProperty),
                                RequireMethod(
                                    runtimeProperty,
                                    "GetIndexedCollectionAccessor",
                                    typeof(IClrIndexedCollectionAccessor),
                                    [])),
                            RequireMethod(
                                typeof(IClrIndexedCollectionAccessor),
                                "Create",
                                typeof(object),
                                [typeof(int)]),
                            currentCount),
                        typeof(IList)),
                    property,
                    currentCount)
                .Compile();
        }

        /// <summary>Closes the exact native Owner operation before the recovery seam is published.</summary>
        private static Func<MemberEntry, object> BindOwner(
            Type nativeBase,
            Type nativeInterface,
            ParameterExpression member
        )
        {
            var memberInfrastructure = RequireInfrastructure(typeof(MemberEntry), nativeInterface);

            // WHY: IInfrastructure<T> is invariant. Bind each exact interface instead of casting a complex
            // native identity through an entity-only interface or resolving an element again by mutable index.
            return Expression
                .Lambda<Func<MemberEntry, object>>(
                    Expression.Convert(
                        Expression.Call(
                            Expression.Convert(member, memberInfrastructure),
                            RequireMethod(memberInfrastructure, "get_Instance", nativeInterface, [])),
                        nativeBase),
                    member)
                .Compile();
        }

        /// <summary>Closes the exact native Collection operation before the recovery seam is published.</summary>
        private static Func<object, IComplexProperty, ComplexCollectionEntry> BindCollection(
            Type nativeInterface,
            ParameterExpression owner,
            ParameterExpression property
        )
        {
            return Expression
                .Lambda<Func<object, IComplexProperty, ComplexCollectionEntry>>(
                    Expression.New(
                        RequireConstructor(typeof(ComplexCollectionEntry), [nativeInterface, typeof(IComplexProperty)]),
                        Expression.Convert(owner, nativeInterface),
                        property),
                    owner,
                    property)
                .Compile();
        }

        /// <summary>Closes the exact native Property operation before the recovery seam is published.</summary>
        private static Func<object, IProperty, PropertyEntry> BindProperty(
            Type nativeInterface,
            ParameterExpression owner,
            ParameterExpression scalar
        )
        {
            return Expression
                .Lambda<Func<object, IProperty, PropertyEntry>>(
                    Expression.New(
                        RequireConstructor(typeof(PropertyEntry), [nativeInterface, typeof(IProperty)]),
                        Expression.Convert(owner, nativeInterface),
                        scalar),
                    owner,
                    scalar)
                .Compile();
        }

        /// <summary>Closes the exact native Entries operation before the recovery seam is published.</summary>
        private static Func<object, IComplexProperty, bool, object?> BindEntries(
            Type nativeBase,
            Type nativeComplex,
            ParameterExpression owner,
            ParameterExpression property,
            ParameterExpression original,
            UnaryExpression nativeOwner
        )
        {
            var lanes = nativeBase.GetField("_complexCollectionEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "EF Core does not expose the expected complex collection cache.");

            var laneType = lanes.FieldType.GetElementType()
                ?? throw new InvalidOperationException("EF Core's complex collection cache is not an array.");

            var currentEntries = laneType.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic);
            var originalEntries = laneType.GetField("_originalEntries", BindingFlags.Instance | BindingFlags.NonPublic);

            if (!laneType.IsValueType
                || currentEntries?.FieldType != typeof(List<>).MakeGenericType(nativeComplex)
                || originalEntries?.FieldType != currentEntries.FieldType)
            {
                throw new InvalidOperationException("EF Core's cached complex entry lanes are incompatible.");
            }

            // WHY: GetComplexCollectionEntries initializes capacity by reading CLR membership. A retained
            // native ordinal can be outside a caller's raw shortened list; read existing cache slots only.
            var lane = Expression.ArrayIndex(
                Expression.Field(nativeOwner, lanes),
                Expression.Call(property, RequireMethod(typeof(IPropertyBase), "GetIndex", typeof(int), [])));

            return Expression
                .Lambda<Func<object, IComplexProperty, bool, object?>>(
                    Expression.Convert(
                        Expression.Condition(
                            original,
                            Expression.Field(lane, originalEntries),
                            Expression.Field(lane, currentEntries)),
                        typeof(object)),
                    owner,
                    property,
                    original)
                .Compile();
        }
    }
}
