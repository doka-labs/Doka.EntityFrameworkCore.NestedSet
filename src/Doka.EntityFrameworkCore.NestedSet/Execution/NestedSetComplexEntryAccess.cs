namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Closes the native EF complex-entry operations needed to recover collection topology.</summary>
/// <remarks>
///     Bindings retain framework metadata only. No model, context, entity or collection is cached here.
///     Restoration uses EF's state and ordinal methods; it never writes private state flags or lane storage.
/// </remarks>
internal static class NestedSetComplexEntryAccess
{
    private static readonly Bindings s_bindings = new();

    /// <summary>Gets the exact native owner of an existing public scalar or complex member entry.</summary>
    /// <param name="entry">The public member whose containing entity or complex element is retained.</param>
    /// <returns>The existing native InternalEntryBase instance, without creating a tracking identity.</returns>
    internal static object Owner(
        MemberEntry entry
    ) => s_bindings.Owner(entry);

    /// <summary>Creates a public complex collection entry on its exact retained native owner.</summary>
    /// <param name="owner">The native containing entity or complex collection element.</param>
    /// <param name="property">The collection metadata belonging to that owner.</param>
    /// <returns>A public member wrapper without changing the native owner's identity.</returns>
    /// <remarks>The caller must suspend automatic detection before constructing EF collection wrappers.</remarks>
    internal static ComplexCollectionEntry Collection(
        object owner,
        IComplexProperty property
    ) => s_bindings.Collection(owner, property);

    /// <summary>Reads an existing native scalar even when a raw-null reference hides its current CLR path.</summary>
    /// <param name="owner">The retained complex element identity.</param>
    /// <param name="property">The exact flattened scalar metadata belonging to that native element.</param>
    /// <returns>A wrapper exposing independent original values and modification intent.</returns>
    internal static PropertyEntry Property(
        object owner,
        IProperty property
    ) => s_bindings.Property(owner, property);

    /// <summary>Observes cached current or original slots without traversing a possibly shortened CLR graph.</summary>
    /// <param name="owner">The native containing entity or complex collection element.</param>
    /// <param name="property">The complex collection whose native ordinal list is read.</param>
    /// <param name="original">Whether to read original entries rather than current entries.</param>
    /// <returns>The existing cached list, or an empty immutable list when that lane was never initialized.</returns>
    /// <remarks>Read-only observation: callers must not mutate the returned list or its ordinal slots.</remarks>
    internal static IList Entries(
        object owner,
        IComplexProperty property,
        bool original
    )
    {
        var result = s_bindings.Entries(owner, property, original);

        if (result is null)
        {
            return Array.Empty<object>();
        }

        if (result is not IList entries)
        {
            throw new InvalidOperationException(
                "EF Core's complex collection entry reader no longer returns an observable IList backing store.");
        }

        return entries;
    }

    /// <summary>Reads one retained element's current or original ordinal through EF's native property.</summary>
    /// <param name="native">The exact native complex element identity.</param>
    /// <param name="original">Whether to read the original ordinal instead of the current ordinal.</param>
    /// <returns>The ordinal, including EF's legitimate absent-lane sentinel of minus one.</returns>
    internal static int Ordinal(
        object native,
        bool original
    ) => s_bindings.Ordinal(native, original);

    /// <summary>Reads the current ordinal path of a native entry through all of its containing elements.</summary>
    /// <param name="native">The native entity or complex element identity.</param>
    /// <returns>A copied path, empty for an entity and possibly containing minus-one sentinels.</returns>
    internal static int[] Ordinals(
        object native
    ) => s_bindings
        .Ordinals(native)
        .ToArray();

    /// <summary>Restores an ordinal through EF's own collision and state validation.</summary>
    /// <param name="native">The exact native complex element identity.</param>
    /// <param name="ordinal">The captured ordinal, including minus one for an absent lane.</param>
    /// <param name="original">Whether to restore the original ordinal rather than the current ordinal.</param>
    internal static void SetOrdinal(
        object native,
        int ordinal,
        bool original
    ) => s_bindings.SetOrdinal(native, ordinal, original);

    /// <summary>Reads the native entity or complex element's state without invoking a public state setter.</summary>
    /// <param name="native">The retained native entry.</param>
    /// <returns>The state currently held by EF.</returns>
    internal static EntityState State(
        object native
    ) => s_bindings.State(native);

    /// <summary>Uses EF's native transition without acceptance or blanket scalar Modified marking.</summary>
    /// <param name="native">The retained native entry.</param>
    /// <param name="state">The captured state restored through EF's normal transition method.</param>
    internal static void SetState(
        object native,
        EntityState state
    )
    {
        if (s_bindings.State(native) != state)
        {
            s_bindings.SetState(native, state);
        }
    }

    /// <summary>Restores exact lane lengths through EF's own complex collection capacity operation.</summary>
    /// <param name="owner">The native containing entity or complex element.</param>
    /// <param name="property">The complex collection whose native entries have been restored.</param>
    /// <param name="current">The captured current-entry count.</param>
    /// <param name="original">The captured original-entry count.</param>
    internal static void Capacity(
        object owner,
        IComplexProperty property,
        int current,
        int original
    ) => s_bindings.Capacity(owner, property, current, original);

    /// <summary>Reads a complex original value using EF's native owner and original-value semantics.</summary>
    /// <param name="owner">The retained native containing entry.</param>
    /// <param name="property">The complex metadata whose original value is retained.</param>
    /// <returns>The native original value, which can legitimately be null.</returns>
    internal static object? Original(
        object owner,
        IComplexProperty property
    ) => s_bindings.Original(owner, property);

    /// <summary>Restores a complex original through EF's comparer snapshot writer without detection.</summary>
    /// <param name="owner">The retained native containing entry.</param>
    /// <param name="property">The complex metadata whose original buffer is restored.</param>
    /// <param name="value">The captured independent original value.</param>
    internal static void RestoreOriginal(
        object owner,
        IComplexProperty property,
        object? value
    ) => NestedSetComplexOriginalValue.Restore(owner, property, value);

    /// <summary>Restores a collection's aggregate flag without recursively changing its element states.</summary>
    /// <param name="owner">The native containing entity or complex element.</param>
    /// <param name="property">The complex collection whose captured flag is restored.</param>
    /// <param name="modified">The captured aggregate modification flag.</param>
    internal static void SetModified(
        object owner,
        IComplexProperty property,
        bool modified
    ) => s_bindings.SetModified(owner, property, modified);

    /// <summary>Gets EF's mapped CLR setter, including containing-entity copyback for complex structs.</summary>
    /// <param name="property">The captured complex container metadata.</param>
    /// <returns>The model-owned setter without creating an alternate reflection-based access strategy.</returns>
    internal static IClrPropertySetter Setter(
        IPropertyBase property
    ) => s_bindings.Setter(property);

    /// <summary>Creates staging storage through EF's mapped indexed collection factory.</summary>
    /// <param name="property">The complex collection metadata selecting its configured CLR collection shape.</param>
    /// <param name="capacity">The staging capacity required for retained native ordinals.</param>
    /// <returns>The mapped collection instance created by EF's own accessor.</returns>
    internal static IList CreateCollection(
        IComplexProperty property,
        int capacity
    ) => s_bindings.CreateCollection(property, capacity);

    /// <summary>Validates and compiles this recovery-only framework seam once for the loaded EF assembly.</summary>
    private sealed class Bindings
    {
        /// <summary>Closes exact native signatures before the first complex snapshot can be accepted.</summary>
        internal Bindings()
        {
            var assembly = typeof(IUpdateEntry).Assembly;
            var nativeBase = RequireType(assembly, "InternalEntryBase");
            var nativeComplex = RequireType(assembly, "InternalComplexEntry");
            var nativeInterface = RequireType(assembly, "IInternalEntry");
            var runtimeProperty =
                assembly.GetType("Microsoft.EntityFrameworkCore.Metadata.Internal.IRuntimePropertyBase")
                ?? throw new InvalidOperationException("EF Core does not expose the expected mapped setter contract.");

            if (!nativeBase.IsAssignableFrom(nativeComplex)
                || !nativeInterface.IsAssignableFrom(nativeBase))
            {
                throw new InvalidOperationException("EF Core's native complex entry inheritance is incompatible.");
            }

            var owner = Expression.Parameter(typeof(object), "owner");
            var complex = Expression.Parameter(typeof(object), "complex");
            var member = Expression.Parameter(typeof(MemberEntry), "member");
            var property = Expression.Parameter(typeof(IComplexProperty), "property");
            var scalar = Expression.Parameter(typeof(IProperty), "scalar");
            var metadata = Expression.Parameter(typeof(IPropertyBase), "metadata");
            var original = Expression.Parameter(typeof(bool), "original");
            var ordinal = Expression.Parameter(typeof(int), "ordinal");
            var state = Expression.Parameter(typeof(EntityState), "state");
            var currentCount = Expression.Parameter(typeof(int), "currentCount");
            var originalCount = Expression.Parameter(typeof(int), "originalCount");
            var modified = Expression.Parameter(typeof(bool), "modified");
            var nativeOwner = Expression.Convert(owner, nativeBase);
            var nativeElement = Expression.Convert(complex, nativeComplex);
            Setter = Expression
                .Lambda<Func<IPropertyBase, IClrPropertySetter>>(
                    Expression.Call(
                        Expression.Convert(metadata, runtimeProperty),
                        RequireMethod(runtimeProperty, "GetSetter", typeof(IClrPropertySetter), [])),
                    metadata)
                .Compile();

            // WHY: Retained ordinals can outlive the current CLR list. Ask EF for correctly typed staging
            // storage instead of assuming List<T>, invoking arbitrary constructors or copying caller storage.
            CreateCollection = Expression
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

            var memberInfrastructure = RequireInfrastructure(typeof(MemberEntry), nativeInterface);

            // WHY: IInfrastructure<T> is invariant. Bind each exact interface instead of casting a complex
            // native identity through an entity-only interface or resolving an element again by mutable index.
            Owner = Expression
                .Lambda<Func<MemberEntry, object>>(
                    Expression.Convert(
                        Expression.Call(
                            Expression.Convert(member, memberInfrastructure),
                            RequireMethod(memberInfrastructure, "get_Instance", nativeInterface, [])),
                        nativeBase),
                    member)
                .Compile();

            Collection = Expression
                .Lambda<Func<object, IComplexProperty, ComplexCollectionEntry>>(
                    Expression.New(
                        RequireConstructor(typeof(ComplexCollectionEntry), [nativeInterface, typeof(IComplexProperty)]),
                        Expression.Convert(owner, nativeInterface),
                        property),
                    owner,
                    property)
                .Compile();

            Property = Expression
                .Lambda<Func<object, IProperty, PropertyEntry>>(
                    Expression.New(
                        RequireConstructor(typeof(PropertyEntry), [nativeInterface, typeof(IProperty)]),
                        Expression.Convert(owner, nativeInterface),
                        scalar),
                    owner,
                    scalar)
                .Compile();

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

            Entries = Expression
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

            var readOrdinal = RequireMethod(nativeComplex, "get_Ordinal", typeof(int), []);
            var readOriginalOrdinal = RequireMethod(nativeComplex, "get_OriginalOrdinal", typeof(int), []);
            Ordinal = Expression
                .Lambda<Func<object, bool, int>>(
                    Expression.Condition(
                        original,
                        Expression.Call(nativeElement, readOriginalOrdinal),
                        Expression.Call(nativeElement, readOrdinal)),
                    complex,
                    original)
                .Compile();

            Ordinals = Expression
                .Lambda<Func<object, IReadOnlyList<int>>>(
                    Expression.Call(
                        nativeOwner,
                        RequireMethod(nativeBase, "GetOrdinals", typeof(IReadOnlyList<int>), [])),
                    owner)
                .Compile();

            SetOrdinal = Expression
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

            State = Expression
                .Lambda<Func<object, EntityState>>(
                    Expression.Call(nativeOwner, RequireMethod(nativeBase, "get_EntityState", typeof(EntityState), [])),
                    owner)
                .Compile();

            // WHY: Public element.State defaults modifyProperties to true. Topology recovery restores flags
            // separately, so retain EF transitions without acceptance or blanket Modified marking.
            SetState = Expression
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
                                typeof(EntityState?)
                            ]),
                        state,
                        Expression.Constant(false),
                        Expression.Constant(false),
                        Expression.Constant(null, typeof(EntityState?)),
                        Expression.Constant(null, typeof(EntityState?))),
                    owner,
                    state)
                .Compile();

            Capacity = Expression
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

            Original = Expression
                .Lambda<Func<object, IComplexProperty, object?>>(
                    Expression.Call(
                        nativeOwner,
                        RequireMethod(nativeBase, "GetOriginalValue", typeof(object), [typeof(IPropertyBase)]),
                        property),
                    owner,
                    property)
                .Compile();

            // WHY: Recursive public collection flag setters can destroy legitimate Added or deleted elements.
            // Restore only this aggregate flag; the journal restores each captured element's state independently.
            SetModified = Expression
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

        /// <summary>Gets the exact member-owner infrastructure reader.</summary>
        internal Func<MemberEntry, object> Owner { get; }

        /// <summary>Gets the exact-owner complex collection wrapper constructor.</summary>
        internal Func<object, IComplexProperty, ComplexCollectionEntry> Collection { get; }

        /// <summary>Gets a scalar wrapper for original/flag recovery beneath a missing current CLR container.</summary>
        internal Func<object, IProperty, PropertyEntry> Property { get; }

        /// <summary>Gets the current/original native backing-list reader.</summary>
        internal Func<object, IComplexProperty, bool, object?> Entries { get; }

        /// <summary>Gets the validated current/original ordinal reader.</summary>
        internal Func<object, bool, int> Ordinal { get; }

        /// <summary>Gets the containing-path ordinal reader.</summary>
        internal Func<object, IReadOnlyList<int>> Ordinals { get; }

        /// <summary>Gets the validated current/original ordinal setter.</summary>
        internal Action<object, int, bool> SetOrdinal { get; }

        /// <summary>Gets the native state reader.</summary>
        internal Func<object, EntityState> State { get; }

        /// <summary>Gets the state transition preserving independent property flags.</summary>
        internal Action<object, EntityState> SetState { get; }

        /// <summary>Gets the native exact-capacity restoration operation.</summary>
        internal Action<object, IComplexProperty, int, int> Capacity { get; }

        /// <summary>Gets the native complex original-value reader.</summary>
        internal Func<object, IComplexProperty, object?> Original { get; }

        /// <summary>Gets the nonrecursive complex collection flag restoration operation.</summary>
        internal Action<object, IComplexProperty, bool> SetModified { get; }

        /// <summary>Gets the model-owned mapped setter through its validated runtime metadata contract.</summary>
        internal Func<IPropertyBase, IClrPropertySetter> Setter { get; }

        /// <summary>Gets the mapped collection factory without repeating native signature reflection.</summary>
        internal Func<IComplexProperty, int, IList> CreateCollection { get; }
    }

    /// <summary>Requires the native type from the same loaded EF assembly as its public update interface.</summary>
    private static Type RequireType(
        Assembly assembly,
        string name
    ) => assembly.GetType($"Microsoft.EntityFrameworkCore.ChangeTracking.Internal.{name}")
        ?? throw new InvalidOperationException($"EF Core does not expose the expected native type '{name}'.");

    /// <summary>Requires the exact invariant infrastructure interface implemented by a public entry wrapper.</summary>
    private static Type RequireInfrastructure(
        Type wrapper,
        Type native
    )
    {
        var infrastructure = typeof(IInfrastructure<>).MakeGenericType(native);

        if (!infrastructure.IsAssignableFrom(wrapper))
        {
            throw new InvalidOperationException(
                $"EF Core's '{wrapper.Name}' does not expose the expected IInfrastructure<{native.Name}> contract.");
        }

        return infrastructure;
    }

    /// <summary>Requires a public instance signature before generating any native delegate adaptation.</summary>
    private static MethodInfo RequireMethod(
        Type owner,
        string name,
        Type result,
        Type[] parameters
    )
    {
        var method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, parameters, null);

        if (method is null
            || method.IsStatic
            || method.ContainsGenericParameters
            || method.ReturnType != result)
        {
            throw new InvalidOperationException(
                $"EF Core's '{owner.Name}.{name}' signature is incompatible with complex topology recovery.");
        }

        return method;
    }

    /// <summary>Requires the exact framework entry constructor without selecting another overload.</summary>
    private static ConstructorInfo RequireConstructor(
        Type owner,
        Type[] parameters
    ) => owner.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, parameters, null)
        ?? throw new InvalidOperationException(
            $"EF Core does not expose the expected '{owner.Name}' constructor for complex topology recovery.");
}
