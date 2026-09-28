namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides an inline variant with explicit, justified engine exclusions.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class EngineInlineDataAttribute : DataAttribute
{
    private readonly object?[] _data;

    /// <summary>Captures provider-independent test arguments.</summary>
    /// <param name="data">The arguments passed to the theory method.</param>
    public EngineInlineDataAttribute(
        params object?[] data
    )
    {
        _data = data;
    }

    /// <summary>Gets or sets engines that cannot execute this particular variant.</summary>
    public string[] ExcludedEngines { get; set; } = [];

    /// <summary>Gets or sets the concrete reason for excluding engines from this variant.</summary>
    public string? Reason { get; set; }

    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(
        MethodInfo testMethod,
        DisposalTracker disposalTracker
    )
    {
        var engine = EngineTestSelection.ResolveEngine(testMethod.ReflectedType!);
        IReadOnlyCollection<ITheoryDataRow> rows = EngineTestSelection.AppliesToEngine(engine, ExcludedEngines, Reason)
            ? [ConvertDataRow(_data)]
            : [];

        return ValueTask.FromResult(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}

/// <summary>Preserves provider-independent member data and explicitly excluded engine variants.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
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
        var rows = await GetSourceData(testMethod, disposalTracker).ConfigureAwait(false);
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
    public ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetSourceData(
        MethodInfo testMethod,
        DisposalTracker disposalTracker
    )
    {
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

/// <summary>Defines a provider-independent member-data variant with explicit, justified engine exclusions.</summary>
public sealed class EngineTheoryDataRow : TheoryDataRow
{
    /// <summary>Captures arguments and the engines unable to execute this specific variant.</summary>
    /// <param name="data">The arguments passed to the theory method.</param>
    /// <param name="excludedEngines">Exact engine names excluded from this variant.</param>
    /// <param name="reason">The concrete reason for those exclusions.</param>
    public EngineTheoryDataRow(
        object?[] data,
        string[] excludedEngines,
        string reason
    ) : base(data)
    {
        ExcludedEngines = excludedEngines;
        Reason = reason;
    }

    /// <summary>Gets the engines explicitly excluded from this variant.</summary>
    public string[] ExcludedEngines { get; }

    /// <summary>Gets the concrete reason for this variant's exclusions.</summary>
    public string Reason { get; }
}

/// <summary>Defines a shared fact with explicit engine exclusions instead of provider opt-in.</summary>
[XunitTestCaseDiscoverer(typeof(EngineFactDiscoverer))]
[AttributeUsage(AttributeTargets.Method)]
public sealed class EngineFactAttribute : FactAttribute
{
    /// <summary>Preserves compiler-supplied locations for Rider and the xUnit runner.</summary>
    /// <param name="sourceFilePath">The compiler-supplied source path.</param>
    /// <param name="sourceLineNumber">The compiler-supplied source line.</param>
    public EngineFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    ) : base(sourceFilePath, sourceLineNumber) { }

    /// <summary>Gets or sets engines that cannot execute this fact.</summary>
    public string[] ExcludedEngines { get; set; } = [];

    /// <summary>Gets or sets the concrete reason for excluding engines from this fact.</summary>
    public string? Reason { get; set; }
}

/// <summary>Defines a shared theory with optional, explicitly justified method or row exclusions.</summary>
[XunitTestCaseDiscoverer(typeof(EngineTheoryDiscoverer))]
[AttributeUsage(AttributeTargets.Method)]
public sealed class EngineTheoryAttribute : TheoryAttribute
{
    /// <summary>Preserves locations and requires genuinely empty or accidentally over-filtered data to fail.</summary>
    /// <param name="sourceFilePath">The compiler-supplied source path.</param>
    /// <param name="sourceLineNumber">The compiler-supplied source line.</param>
    public EngineTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    ) : base(sourceFilePath, sourceLineNumber)
    {
        SkipTestWithoutData = false;
    }

    /// <summary>Gets or sets engines that cannot execute any variant of this theory.</summary>
    public string[] ExcludedEngines { get; set; } = [];

    /// <summary>Gets or sets the concrete reason for excluding engines from the whole theory.</summary>
    public string? Reason { get; set; }
}

/// <summary>Omits explicitly excluded facts before xUnit constructs a test case or any fixture.</summary>
public sealed class EngineFactDiscoverer : FactDiscoverer
{
    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute
    )
    {
        var engine = EngineTestSelection.ResolveEngine(testMethod.TestClass.Class);

        if (factAttribute is EngineFactAttribute conditional
            && !EngineTestSelection.AppliesToEngine(engine, conditional.ExcludedEngines, conditional.Reason))
        {
            return ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>([]);
        }

        return base.Discover(discoveryOptions, testMethod, factAttribute);
    }
}

/// <summary>Omits excluded methods while preserving xUnit's immediate and delayed data-enumeration behavior.</summary>
public sealed class EngineTheoryDiscoverer : TheoryDiscoverer
{
    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute
    )
    {
        var engine = EngineTestSelection.ResolveEngine(testMethod.TestClass.Class);

        if (factAttribute is EngineTheoryAttribute conditional
            && !EngineTestSelection.AppliesToEngine(engine, conditional.ExcludedEngines, conditional.Reason))
        {
            return ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>([]);
        }

        // WHY: Do not pre-read factories to decide ownership; xUnit must enumerate each source only in its own phase.
        return base.Discover(discoveryOptions, testMethod, factAttribute);
    }
}
