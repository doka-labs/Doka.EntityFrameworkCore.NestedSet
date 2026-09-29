namespace Doka.EntityFrameworkCore.NestedSet.Samples;

/// <summary>Provides readable, cancellation-aware output without hiding domain operations.</summary>
internal static class SampleConsole
{
    /// <summary>Writes one output line using the asynchronous cancellation-aware overload.</summary>
    /// <param name="text">The line to display.</param>
    /// <param name="cancellationToken">The token canceling the awaited output.</param>
    /// <returns>A task completing when the line has been written.</returns>
    public static Task WriteLineAsync(
        string text,
        CancellationToken cancellationToken
    ) => Console.Out.WriteLineAsync(text.AsMemory(), cancellationToken);

    /// <summary>Separates a selected scenario or before/after state from the preceding output.</summary>
    /// <param name="heading">The label describing the next displayed state.</param>
    /// <param name="cancellationToken">The token canceling the awaited output.</param>
    /// <returns>A task completing when the heading has been written.</returns>
    public static async Task HeadingAsync(
        string heading,
        CancellationToken cancellationToken
    )
    {
        await WriteLineAsync(string.Empty, cancellationToken);
        await WriteLineAsync(heading, cancellationToken);
    }

    /// <summary>Reports a precise failed sample expectation instead of a combined generic assertion.</summary>
    /// <param name="condition">Whether the documented result was observed.</param>
    /// <param name="message">The specific result that did not match.</param>
    /// <exception cref="InvalidOperationException">The result differs from the documented scenario.</exception>
    public static void Require(
        bool condition,
        string message
    )
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
