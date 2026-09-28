namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Recognizes assignment targets in provider-generated UPDATE statements used by command observers.</summary>
internal static partial class SqlAssignments
{
    /// <summary>Checks a SET target while ignoring columns read by CASE predicates and SQL keywords.</summary>
    /// <param name="commandText">The generated UPDATE command with provider-specific identifier quoting.</param>
    /// <param name="columnName">The mapped store column whose assignment is being observed.</param>
    /// <returns>Whether the command assigns that column.</returns>
    internal static bool Assigns(
        string commandText,
        string columnName
    )
    {
        // WHY: An unquoted substring such as End also matches CASE END; only assignment targets prove a write.
        foreach (System.Text.RegularExpressions.Match match in AssignmentTargets().Matches(commandText))
        {
            if (string.Equals(match.Groups["column"].Value, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Matches SET and comma-separated targets with optional aliases and all supported SQL quoting.</summary>
    /// <returns>The cached generated matcher; it does not interpret right-hand expression identifiers.</returns>
    [System.Text.RegularExpressions.GeneratedRegex(
        "(?:\\bSET\\s+|,\\s*)(?:(?:\"[^\"]+\"|`[^`]+`|\\[[^\\]]+\\]|\\w+)\\s*\\.\\s*)?"
        + "(?:\"(?<column>[^\"]+)\"|`(?<column>[^`]+)`|\\[(?<column>[^\\]]+)\\]|(?<column>\\w+))\\s*=",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        1000)]
    private static partial System.Text.RegularExpressions.Regex AssignmentTargets();
}
