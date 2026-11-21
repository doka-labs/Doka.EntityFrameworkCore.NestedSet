namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves provider-independent member data and explicitly excluded engine variants.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class EngineMemberDataAttribute : MemberDataAttributeBase
{
    /// <summary>Uses xUnit's normal public static member resolution.</summary>
    /// <param name="memberName">The public static member supplying rows.</param>
    /// <param name="arguments">Arguments passed to a member-data factory method.</param>
    public EngineMemberDataAttribute(
        string memberName,
        params object?[] arguments
    ) : base(memberName, arguments) { }

    /// <summary>Gets or sets engines excluded from all variants supplied by this member.</summary>
    public string[] ExcludedEngines { get; set; } = [];

    /// <summary>Gets or sets the concrete reason for exclusions applying to the whole member.</summary>
    public string? Reason { get; set; }

    /// <inheritdoc />
    public override async ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(
        MethodInfo testMethod,
        DisposalTracker disposalTracker
    )
    {
        var engine = EngineTestSelection.ResolveEngine(testMethod.ReflectedType!);
        var includeMember = EngineTestSelection.AppliesToEngine(engine, ExcludedEngines, Reason);
        var rows = await GetSourceData(
                testMethod,
                disposalTracker,
                cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        var included = new List<ITheoryDataRow>(rows.Count);

        foreach (var row in rows)
        {
            // WHY: Validate every variant even when the whole member is excluded, so invalid metadata cannot vanish.
            var includeRow = row is not EngineTheoryDataRow conditional
                || EngineTestSelection.AppliesToEngine(engine, conditional.ExcludedEngines, conditional.Reason);

            if (includeMember && includeRow)
            {
                included.Add(row);
            }
        }

        return included;
    }

    /// <summary>Reads unfiltered rows once so guards can inspect every explicit exclusion.</summary>
    /// <param name="testMethod">The concrete reflected theory method.</param>
    /// <param name="disposalTracker">The runner's owner for data-row resources.</param>
    /// <param name="cancellationToken">The caller's token checked before framework data dispatch.</param>
    public ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetSourceData(
        MethodInfo testMethod,
        DisposalTracker disposalTracker,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: Inherited static factories belong to the concrete reflected suite unless MemberType overrides it.
        MemberType ??= testMethod.ReflectedType;

        return base.GetData(testMethod, disposalTracker);
    }

    /// <inheritdoc />
    protected override ITheoryDataRow ConvertDataRow(
        object dataRow
    )
    {
        var normalized = base.ConvertDataRow(dataRow);

        if (dataRow is not EngineTheoryDataRow conditional)
        {
            return normalized;
        }

        // WHY: xUnit normalizes ITheoryDataRow to its own type; copy merged metadata while preserving exclusions.
        return new EngineTheoryDataRow(normalized.GetData(), conditional.ExcludedEngines, conditional.Reason)
        {
            DisableParallelization = normalized.DisableParallelization,
            Explicit = normalized.Explicit,
            Label = normalized.Label,
            Skip = normalized.Skip,
            TestDisplayName = normalized.TestDisplayName,
            Timeout = normalized.Timeout,
            Traits = normalized.Traits ?? new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
        };
    }
}
