namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetInsertionContract
{
    /// <summary>Validates every used native member and scalar, composite and nullable generic map shapes.</summary>
    internal static void Validate() => _ = Instance;

    /// <summary>Binds an assembly using the same complete resolver used by options registration.</summary>
    /// <param name="assembly">The loaded EF assembly whose exact member shapes must match.</param>
    /// <returns>The validated model-free delegates for this assembly.</returns>
    internal static NestedSetInsertionContract Bind(
        Assembly assembly
    ) => new(assembly);

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
                && candidate.GetGenericArguments() is
                    [{ GenericParameterAttributes: GenericParameterAttributes.None } argument]
                && argument.GetGenericParameterConstraints()
                    .Length
                == 0
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

            // WHY: Compare actual and required parameter types at the same signature ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
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
        var version = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? _assembly
                .GetName()
                .Version
                ?.ToString()
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
