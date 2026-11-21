namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class ProviderTestContract
{
    /// <summary>Matches public inherited local methods while retaining the concrete leaf as ReflectedType.</summary>
    private static MethodInfo[] LocalMethods(
        Type type
    ) => type
        .GetMethods(
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.FlattenHierarchy)
        .Where(method => method.DeclaringType!.Assembly == type.Assembly)
        .ToArray();

    /// <summary>Gets only declarations belonging to this exact suite.</summary>
    private static MethodInfo[] DeclaredMethods(
        Type type
    ) => type.GetMethods(
        BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly);

    /// <summary>Recognizes facts and theories without constructing potentially invalid annotations.</summary>
    private static bool IsTestMethod(
        MethodInfo method
    ) => method.CustomAttributes.Any(attribute => typeof(IFactAttribute).IsAssignableFrom(attribute.AttributeType));

    /// <summary>Reads every source row without applying current-engine discovery selection.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static ValueTask<IReadOnlyCollection<ITheoryDataRow>> ReadRawMemberRowsAsync(
        MemberDataAttributeBase member,
        MethodInfo method,
        DisposalTracker tracker,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: xUnit supplies the concrete ReflectedType for an unset MemberType; explicit sources stay intact.
        member.MemberType ??= method.ReflectedType;

        return member switch
        {
            EngineMemberDataAttribute conditional => conditional.GetSourceData(
                method,
                tracker,
                cancellationToken: cancellationToken),
            _ => member.GetData(method, tracker),
        };
    }

    /// <summary>Extracts method exclusions from raw metadata before constructing custom annotations.</summary>
    private static string?[] MethodExclusions(
        MethodInfo method
    ) => method
        .CustomAttributes
        .Where(attribute => typeof(IFactAttribute).IsAssignableFrom(attribute.AttributeType))
        .SelectMany(AttributeExclusions)
        .ToArray();

    /// <summary>Reads declared exclusions independently of discovery's filtered attribute objects.</summary>
    private static string?[] AttributeExclusions(
        CustomAttributeData attribute
    )
    {
        var values = attribute.NamedArguments.FirstOrDefault(argument =>
                argument.MemberName == nameof(EngineFactAttribute.ExcludedEngines))
            .TypedValue.Value as IReadOnlyCollection<CustomAttributeTypedArgument>;

        return values
                ?.Select(value => value.Value as string)
                .ToArray()
            ?? [];
    }

    /// <summary>Gets member exclusions which apply before any row-specific conditions.</summary>
    private static string[] MemberExclusions(
        MemberDataAttributeBase member
    ) => member is EngineMemberDataAttribute engine
        ? engine.ExcludedEngines
        : [];

    /// <summary>Gets the additional conditions carried by one normalized source variant.</summary>
    private static string[] RowExclusions(
        ITheoryDataRow row
    ) => row is EngineTheoryDataRow engine
        ? engine.ExcludedEngines
        : [];

    /// <summary>Identifies the exact declaration in a guard failure.</summary>
    private static string Identity(
        MethodInfo method
    ) => $"{method.DeclaringType!.FullName}.{method.Name}";
}
