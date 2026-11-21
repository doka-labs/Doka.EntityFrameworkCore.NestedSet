namespace Doka.EntityFrameworkCore.NestedSet.Tests;

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
