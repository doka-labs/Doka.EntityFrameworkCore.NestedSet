namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Identifies one structural violation in a tree-bound validation report.</summary>
[PublicAPI]
public sealed class NestedSetValidationIssue
{
    /// <summary>Initializes one immutable structural issue.</summary>
    /// <param name="code">The stable machine-readable violation category.</param>
    /// <param name="nodeKey">The offending node key, or <see langword="null" /> for a tree-wide issue.</param>
    /// <param name="message">A payload-free explanation suitable for diagnostics and operator reports.</param>
    internal NestedSetValidationIssue(
        NestedSetValidationCode code,
        object? nodeKey,
        string message
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        Code = code;
        NodeKey = nodeKey;
        Message = message;
    }

    /// <summary>Gets the stable machine-readable violation category.</summary>
    public NestedSetValidationCode Code { get; }

    /// <summary>Gets the offending node key, or <see langword="null" /> for a tree-wide issue.</summary>
    public object? NodeKey { get; }

    /// <summary>Gets the payload-free explanation suitable for diagnostics and operator reports.</summary>
    public string Message { get; }
}
