namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Represents application data that must survive rejected resets and read-only inspection.</summary>
internal sealed class SampleLifecycleSentinel
{
    /// <summary>Gets or sets the explicitly assigned fixture row identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the value used to identify preserved application data.</summary>
    public string Value { get; set; } = string.Empty;
}
