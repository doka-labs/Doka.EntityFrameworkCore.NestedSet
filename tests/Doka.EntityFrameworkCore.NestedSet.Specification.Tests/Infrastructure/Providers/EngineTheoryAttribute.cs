namespace Doka.EntityFrameworkCore.NestedSet.Tests;

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
