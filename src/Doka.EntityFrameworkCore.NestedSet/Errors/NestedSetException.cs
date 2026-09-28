namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Reports a hierarchy precondition failure with a stable, machine-readable code.</summary>
/// <remarks>
///     Provider, cancellation, and arithmetic exceptions retain their original types. This exception derives from
///     InvalidOperationException so existing handlers for rejected hierarchy operations continue to work.
/// </remarks>
public sealed class NestedSetException : InvalidOperationException
{
    /// <summary>Creates an unclassified hierarchy precondition failure.</summary>
    public NestedSetException() : this(
        NestedSetErrorCode.OperationRejected,
        "The nested-set operation was rejected.") { }

    /// <summary>Creates an unclassified hierarchy precondition failure with a message.</summary>
    /// <param name="message">The human-readable reason for rejection.</param>
    public NestedSetException(
        string? message
    ) : this(NestedSetErrorCode.OperationRejected, message) { }

    /// <summary>Creates an unclassified failure while retaining the original cause.</summary>
    /// <param name="message">The human-readable reason for rejection.</param>
    /// <param name="innerException">The original exception, if any.</param>
    public NestedSetException(
        string? message,
        Exception? innerException
    ) : this(NestedSetErrorCode.OperationRejected, message, innerException) { }

    /// <summary>Creates a classified hierarchy precondition failure.</summary>
    /// <param name="code">The stable reason that callers may handle without parsing messages.</param>
    /// <param name="message">The human-readable reason for rejection.</param>
    /// <param name="innerException">The original exception, if any.</param>
    public NestedSetException(
        NestedSetErrorCode code,
        string? message,
        Exception? innerException = null
    ) : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Gets the stable reason for rejecting the operation.</summary>
    /// <value>A code independent of message wording or localization.</value>
    public NestedSetErrorCode Code { get; }
}
