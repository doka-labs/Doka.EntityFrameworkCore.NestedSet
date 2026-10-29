namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Identifies commands that contain public hierarchy composition for adapter-owned plan policies.</summary>
internal static class NestedSetCompositionTag
{
    /// <summary>The EF query tag emitted as a leading SQL comment line.</summary>
    internal const string Text = "Doka.EntityFrameworkCore.NestedSet composition";

    private const string CommentLine = "-- " + Text;

    /// <summary>Checks only EF's leading tag comment block, never comments or literals inside the statement.</summary>
    /// <param name="commandText">The generated command text.</param>
    /// <returns>Whether EF emitted this library tag for the command.</returns>
    internal static bool Marks(
        string commandText
    )
    {
        var remaining = commandText.AsSpan();

        while (remaining.StartsWith("--", StringComparison.Ordinal))
        {
            var end = remaining.IndexOfAny('\r', '\n');
            var line = end < 0 ? remaining : remaining[..end];

            if (line.TrimEnd().Equals(CommentLine, StringComparison.Ordinal))
            {
                return true;
            }

            remaining = end < 0 ? [] : remaining[end..].TrimStart("\r\n");
        }

        return false;
    }
}
