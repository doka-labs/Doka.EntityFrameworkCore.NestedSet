namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Binds the model-free EF insertion seams before options registration can complete.</summary>
internal sealed class NestedSetInsertionContract
{
    private static readonly Lazy<NestedSetInsertionContract>
        s_instance = new(() => Bind(typeof(IUpdateEntry).Assembly));

    private readonly Assembly _assembly;
    private readonly Type _entryType;
    private readonly MethodInfo _relationshipSnapshot;
    private readonly ConditionalWeakTable<Type, KeyMap> _maps = new();
    private readonly ConditionalWeakTable<Type, KeyMap>.CreateValueCallback _createMap;

    /// <summary>Gets the once-validated contract without retaining any application context or model.</summary>
    internal static NestedSetInsertionContract Instance => s_instance.Value;

    /// <summary>Reads EF's invariant entry identity through its exact infrastructure interface.</summary>
    internal Func<EntityEntry, IUpdateEntry> Entry { get; }

    /// <summary>Reads the context-local manager belonging to an introduced entry.</summary>
    internal Func<IUpdateEntry, object> Manager { get; }

    /// <summary>Restores a shadow key through EF's setter with setModified disabled.</summary>
    internal Action<IUpdateEntry, IProperty, object?> SetProperty { get; }

    /// <summary>Finds an existing native identity map without creating one.</summary>
    internal Func<object, IKey, object?> FindMap { get; }

    /// <summary>Reads an exact native foreign-key bucket through its public update-entry result.</summary>
    internal Func<object, IReadOnlyList<object?>, IEnumerable<IUpdateEntry>> Dependents { get; }

    /// <summary>Removes a partially installed entry through EF's normal identity cleanup.</summary>
    internal Action<object, IUpdateEntry, EntityState> StopTracking { get; }

    /// <summary>Removes exact partial reference registrations without a fabricated state transition.</summary>
    internal Action<object, IUpdateEntry, EntityState, EntityState?> UpdateReferences { get; }

    /// <summary>Gets EF's metadata-owned setter without duplicating complex value copyback.</summary>
    internal Func<IPropertyBase, IClrPropertySetter> Setter { get; }

    /// <summary>Validates every used native member and scalar, composite and nullable generic map shapes.</summary>
    internal static void Validate() => _ = Instance;

    /// <summary>Binds an assembly using the same complete resolver used by options registration.</summary>
    /// <param name="assembly">The loaded EF assembly whose exact member shapes must match.</param>
    /// <returns>The validated model-free delegates for this assembly.</returns>
    internal static NestedSetInsertionContract Bind(
        Assembly assembly
    ) => new(assembly);

    /// <summary>Resolves the loaded framework once and validates declared returns before delegate adaptation.</summary>
    private NestedSetInsertionContract(
        Assembly assembly
    )
    {
        _assembly = assembly;
        _createMap = CreateKeyMap;
        _entryType = NativeType("InternalEntityEntry");
        _ = RequireValueMethod(typeof(IUpdateEntry), nameof(IUpdateEntry.GetCurrentValue));
        _relationshipSnapshot = RequireValueMethod(_entryType, "GetRelationshipSnapshotValue");

        var managerType = NativeType("StateManager");
        var infrastructure = typeof(IInfrastructure<>).MakeGenericType(_entryType);
        var entryGetter = RequireMethod(infrastructure, "get_Instance", _entryType, []);
        var managerGetter = RequireMethod(_entryType, "get_StateManager", NativeType("IStateManager"), []);
        var entry = Expression.Parameter(typeof(EntityEntry), "entry");
        var update = Expression.Parameter(typeof(IUpdateEntry), "update");
        var manager = Expression.Parameter(typeof(object), "manager");
        var property = Expression.Parameter(typeof(IProperty), "property");
        var value = Expression.Parameter(typeof(object), "value");
        var nativeEntry = Expression.Convert(update, _entryType);
        var nativeManager = Expression.Convert(manager, managerType);

        Entry = Expression
            .Lambda<Func<EntityEntry, IUpdateEntry>>(
                Expression.Convert(
                    Expression.Call(Expression.Convert(entry, infrastructure), entryGetter),
                    typeof(IUpdateEntry)),
                entry)
            .Compile();

        Manager = Expression
            .Lambda<Func<IUpdateEntry, object>>(
                Expression.Convert(Expression.Call(nativeEntry, managerGetter), typeof(object)),
                update)
            .Compile();

        var setProperty = RequireMethod(
            _entryType,
            "SetProperty",
            typeof(void),
            [
                typeof(IPropertyBase),
                typeof(object),
                typeof(bool),
                typeof(bool),
                typeof(bool)
            ]);

        SetProperty = Expression
            .Lambda<Action<IUpdateEntry, IProperty, object?>>(
                Expression.Call(
                    nativeEntry,
                    setProperty,
                    property,
                    value,
                    Expression.Constant(false),
                    Expression.Constant(false),
                    Expression.Constant(false)),
                update,
                property,
                value)
            .Compile();

        var key = Expression.Parameter(typeof(IKey), "key");
        var findMap = RequireMethod(managerType, "FindIdentityMap", NativeType("IIdentityMap"), [typeof(IKey)]);

        FindMap = Expression
            .Lambda<Func<object, IKey, object?>>(
                Expression.Convert(Expression.Call(nativeManager, findMap, key), typeof(object)),
                manager,
                key)
            .Compile();

        var map = Expression.Parameter(typeof(object), "map");
        var values = Expression.Parameter(typeof(IReadOnlyList<object>), "values");
        var dependentType = NativeType("IDependentsMap");
        var getDependents = RequireMethod(
            dependentType,
            "GetDependents",
            typeof(IEnumerable<IUpdateEntry>),
            [typeof(IReadOnlyList<object>)]);

        Dependents = Expression
            .Lambda<Func<object, IReadOnlyList<object?>, IEnumerable<IUpdateEntry>>>(
                Expression.Call(Expression.Convert(map, dependentType), getDependents, values),
                map,
                values)
            .Compile();

        var state = Expression.Parameter(typeof(EntityState), "state");
        var oldState = Expression.Parameter(typeof(EntityState?), "oldState");
        var stop = RequireMethod(managerType, "StopTracking", typeof(void), [_entryType, typeof(EntityState)]);
        var references = RequireMethod(
            managerType,
            "UpdateReferenceMaps",
            typeof(void),
            [_entryType, typeof(EntityState), typeof(EntityState?)]);

        StopTracking = Expression
            .Lambda<Action<object, IUpdateEntry, EntityState>>(
                Expression.Call(nativeManager, stop, nativeEntry, state),
                manager,
                update,
                state)
            .Compile();
        UpdateReferences = Expression
            .Lambda<Action<object, IUpdateEntry, EntityState, EntityState?>>(
                Expression.Call(nativeManager, references, nativeEntry, state, oldState),
                manager,
                update,
                state,
                oldState)
            .Compile();

        var runtimeProperty = RequireType("Microsoft.EntityFrameworkCore.Metadata.Internal.IRuntimePropertyBase");
        var getSetter = RequireMethod(runtimeProperty, "GetSetter", typeof(IClrPropertySetter), []);
        var metadata = Expression.Parameter(typeof(IPropertyBase), "metadata");
        _ = RequireMethod(
            typeof(IClrPropertySetter),
            nameof(IClrPropertySetter.SetClrValueUsingContainingEntity),
            typeof(void),
            [typeof(object), typeof(object)]);

        Setter = Expression
            .Lambda<Func<IPropertyBase, IClrPropertySetter>>(
                Expression.Call(Expression.Convert(metadata, runtimeProperty), getSetter),
                metadata)
            .Compile();

        // WHY: Resolving only non-generic members would defer inherited protected map/factory failures until
        // a first insert. Validate value, reference, mutable, compound and nullable map signatures here;
        // only model-consumed map types need compiled operations in the weak type cache.
        Type[] keyTypes =
        [
            typeof(int),
            typeof(Guid),
            typeof(string),
            typeof(byte[]),
            typeof(IReadOnlyList<object>)
        ];

        foreach (var definition in new[] { NativeType("IdentityMap`1"), NativeType("NullableKeyIdentityMap`1") })
        {
            foreach (var keyType in keyTypes)
            {
                _ = RequireKeyMap(definition.MakeGenericType(keyType));
            }
        }
    }

    /// <summary>Compiles a model-owned typed reader for the exact native relationship snapshot.</summary>
    /// <typeparam name="TValue">The shared model and member value type.</typeparam>
    /// <param name="property">The model property whose metadata the reader captures.</param>
    /// <returns>A reader without an object adaptation of the snapshot value.</returns>
    internal Func<IUpdateEntry, TValue> RelationshipReader<TValue>(
        IProperty property
    )
    {
        var entry = Expression.Parameter(typeof(IUpdateEntry), "entry");
        var read = Expression.Call(
            Expression.Convert(entry, _entryType),
            _relationshipSnapshot.MakeGenericMethod(typeof(TValue)),
            Expression.Constant(property, typeof(IPropertyBase)));

        return Expression
            .Lambda<Func<IUpdateEntry, TValue>>(read, entry)
            .Compile();
    }

    /// <summary>Gets immutable compiled operations for an exact closed framework map type.</summary>
    internal KeyMap Map(
        Type type
    ) => _maps.GetValue(type, _createMap);

    /// <summary>Checks complete native signatures before adapting native entries and object factory results.</summary>
    private KeyMap CreateKeyMap(
        Type type
    )
    {
        var members = RequireKeyMap(type);
        var map = Expression.Parameter(typeof(object), "map");
        var values = Expression.Parameter(typeof(IReadOnlyList<object>), "values");
        var entry = Expression.Parameter(typeof(IUpdateEntry), "entry");
        var native = Expression.Convert(map, type);
        var nativeEntry = Expression.Convert(entry, _entryType);
        var foreignKey = Expression.Parameter(typeof(IForeignKey), "foreignKey");
        var createdKey = Expression.Convert(
            Expression.Call(Expression.Call(native, members.Factory), members.Create, values),
            members.KeyType);

        return new KeyMap(
            Expression
                .Lambda<Action<object, IReadOnlyList<object?>, IUpdateEntry>>(
                    Expression.Call(native, members.Remove, createdKey, nativeEntry),
                    map,
                    values,
                    entry)
                .Compile(),
            Expression
                .Lambda<Action<object, IUpdateEntry>>(
                    Expression.Call(native, members.RemoveCurrent, nativeEntry),
                    map,
                    entry)
                .Compile(),
            Expression
                .Lambda<Action<object, IReadOnlyList<object?>, IUpdateEntry>>(
                    Expression.Call(native, members.Add, values, nativeEntry),
                    map,
                    values,
                    entry)
                .Compile(),
            Expression
                .Lambda<Func<object, IForeignKey, object?>>(
                    Expression.Convert(Expression.Call(native, members.Dependents, foreignKey), typeof(object)),
                    map,
                    foreignKey)
                .Compile(),
            Expression
                .Lambda<Func<object, IReadOnlyList<object?>, IUpdateEntry?>>(
                    Expression.Convert(Expression.Call(native, members.Find, values), typeof(IUpdateEntry)),
                    map,
                    values)
                .Compile(),
            Expression
                .Lambda<Func<object, IEnumerable<IUpdateEntry>>>(
                    Expression.Convert(Expression.Call(native, members.Entries), typeof(IEnumerable<IUpdateEntry>)),
                    map)
                .Compile());
    }

    /// <summary>Validates closed map and inherited factory signatures without compiling or caching delegates.</summary>
    private KeyMapMembers RequireKeyMap(
        Type type
    )
    {
        if (type.GenericTypeArguments is not [{ } keyType])
        {
            throw MissingContract(type.FullName + ".TKey");
        }

        var factoryType = RequireType("Microsoft.EntityFrameworkCore.ChangeTracking.IPrincipalKeyValueFactory`1")
            .MakeGenericType(keyType);

        return new KeyMapMembers(
            keyType,
            RequireMethod(type, "get_PrincipalKeyValueFactory", factoryType, []),
            RequireMethod(factoryType, "CreateFromKeyValues", typeof(object), [typeof(IReadOnlyList<object>)]),
            RequireMethod(type, "Remove", typeof(void), [keyType, _entryType]),
            RequireMethod(type, "Remove", typeof(void), [_entryType]),
            RequireMethod(type, "Add", typeof(void), [typeof(IReadOnlyList<object>), _entryType]),
            RequireMethod(type, "FindDependentsMap", NativeType("IDependentsMap"), [typeof(IForeignKey)]),
            RequireMethod(type, "TryGetEntry", _entryType, [typeof(IReadOnlyList<object>)]),
            RequireMethod(type, "All", typeof(IEnumerable<>).MakeGenericType(_entryType), []));
    }

    /// <summary>Requires an unconstrained generic value getter with its exact metadata parameter and return.</summary>
    private MethodInfo RequireValueMethod(
        Type type,
        string name
    )
    {
        var method = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == name
                && candidate.IsGenericMethodDefinition
                && candidate.GetGenericArguments() is [{ GenericParameterAttributes: GenericParameterAttributes.None } argument]
                && argument.GetGenericParameterConstraints().Length == 0
                && candidate.ReturnType == argument
                && candidate.GetParameters() is [{ ParameterType: var parameter }]
                && parameter == typeof(IPropertyBase));

        return method ?? throw MissingContract(type.FullName + "." + name + "<TProperty>(IPropertyBase) : TProperty");
    }

    /// <summary>Requires a non-generic native overload with exact parameters and its declared return type.</summary>
    private MethodInfo RequireMethod(
        Type type,
        string name,
        Type result,
        Type[] parameters
    )
    {
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != name
                || method.IsGenericMethod
                || method.ReturnType != result)
            {
                continue;
            }

            var actual = method.GetParameters();

            if (actual.Length != parameters.Length)
            {
                continue;
            }

            var matches = true;

            for (var index = 0; index < parameters.Length; index++)
            {
                matches &= actual[index].ParameterType == parameters[index];
            }

            if (matches)
            {
                return method;
            }
        }

        throw MissingContract(
            type.FullName
            + "."
            + name
            + "("
            + string.Join(", ", parameters.Select(parameter => parameter.FullName ?? parameter.Name))
            + ") : "
            + (result.FullName ?? result.Name));
    }

    /// <summary>Requires an exact native change-tracking type from the assembly being validated.</summary>
    private Type NativeType(
        string name
    ) => RequireType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal." + name);

    /// <summary>Names a missing framework type as a registration incompatibility.</summary>
    private Type RequireType(
        string name
    ) => _assembly.GetType(name) ?? throw MissingContract(name);

    /// <summary>Identifies the loaded patch and required member before an application context exists.</summary>
    private InvalidOperationException MissingContract(
        string member
    )
    {
        var version = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? _assembly.GetName().Version?.ToString()
            ?? "unknown";

        return new InvalidOperationException(
            $"EF Core '{version}' does not expose the required insertion contract member '{member}'. "
            + "Use a qualified EF Core version before configuring UseNestedSets.");
    }

    /// <summary>Shares exact native map operations without retaining entries, maps, models or contexts.</summary>
    internal sealed record KeyMap(
        Action<object, IReadOnlyList<object?>, IUpdateEntry> Remove,
        Action<object, IUpdateEntry> RemoveCurrent,
        Action<object, IReadOnlyList<object?>, IUpdateEntry> Add,
        Func<object, IForeignKey, object?> Dependents,
        Func<object, IReadOnlyList<object?>, IUpdateEntry?> Find,
        Func<object, IEnumerable<IUpdateEntry>> Entries
    );

    /// <summary>Passes validated signatures from the resolver to model-consumed delegate compilation.</summary>
    private sealed record KeyMapMembers(
        Type KeyType,
        MethodInfo Factory,
        MethodInfo Create,
        MethodInfo Remove,
        MethodInfo RemoveCurrent,
        MethodInfo Add,
        MethodInfo Dependents,
        MethodInfo Find,
        MethodInfo Entries
    );
}
