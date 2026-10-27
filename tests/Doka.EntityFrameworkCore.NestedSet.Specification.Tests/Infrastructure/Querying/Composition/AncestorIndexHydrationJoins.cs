using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Reads the two projected Id payload edges in the fixed ancestor-index SQL fixtures.</summary>
internal static partial class AncestorIndexHydrationJoins
{
    /// <summary>Follows each exact projected alias to its immediate direct or filtered-derived join.</summary>
    /// <param name="sql">The emitted fixture SQL with source and owner Id as its first two final columns.</param>
    /// <param name="physicalTable">The known quoted fixture table backing both payload aliases.</param>
    /// <returns>The source and owner join kinds, without treating nested joins as the payload edge.</returns>
    internal static (string Source, string Owner) Read(
        string sql,
        string physicalTable
    )
    {
        if (!IdentifierRegex().IsMatch(physicalTable))
        {
            throw new ArgumentException("The fixture table must be a simple quoted identifier.", nameof(physicalTable));
        }

        // WHY: These fixtures deliberately project Source/Owner Id first. A positional record omits
        // SQL column aliases; the anonymous Unit control names them. Both identify the same payload roles.
        var projections = ProjectionsRegex().Matches(sql);

        if (projections.Count != 1)
        {
            throw new FormatException("The fixture's final source/owner projection is missing or ambiguous.");
        }

        var projection = projections[0];

        return (ReadEdge(sql, projection.Groups["source"].Value, physicalTable),
            ReadEdge(sql, projection.Groups["owner"].Value, physicalTable));
    }

    /// <summary>Matches one exact relation alias and inspects only the immediately preceding source term.</summary>
    private static string ReadEdge(
        string sql,
        string alias,
        string physicalTable
    )
    {
        var aliases = Regex.Matches(sql, $@"\bAS\s+`{Regex.Escape(alias)}`\s+ON\b");

        if (aliases.Count != 1)
        {
            throw new FormatException("The fixture payload alias is missing or reused in another SQL scope.");
        }

        var end = aliases[0].Index - 1;

        while (end >= 0
               && char.IsWhiteSpace(sql[end]))
        {
            end--;
        }

        int start;

        if (end >= 0
            && sql[end] == ')')
        {
            start = OpeningParenthesis(sql, end);
            var term = sql[start..(end + 1)];
            var tables = TablesRegex()
                .Matches(term);

            if (!SelectRegex().IsMatch(term)
                || tables.Count != 1
                || tables[0].Groups["table"].Value != physicalTable)
            {
                throw new FormatException("The filtered payload term does not have the expected fixture-table source.");
            }
        }
        else
        {
            var table = "`" + physicalTable + "`";
            start = end - table.Length + 1;

            if (start < 0
                || !sql
                    .AsSpan(start, table.Length)
                    .SequenceEqual(table))
            {
                throw new FormatException("The direct payload term does not name the expected fixture table.");
            }
        }

        var join = JoinRegex()
            .Match(sql[..start]);

        if (!join.Success)
        {
            throw new FormatException("The payload term does not have an immediate supported join edge.");
        }

        return join.Groups["kind"].Value == "STRAIGHT_JOIN" ? "STRAIGHT_JOIN" : "INNER JOIN";
    }

    /// <summary>Finds the matching source boundary without confusing nested parentheses or quoted fixture identifiers.</summary>
    private static int OpeningParenthesis(
        string sql,
        int end
    )
    {
        var depth = 0;
        var quoted = false;

        for (var index = end; index >= 0; index--)
        {
            var value = sql[index];

            if (value == '`')
            {
                quoted = !quoted;
            }
            else if (!quoted)
            {
                // WHY: Filtered payloads in these fixed fixtures contain identifiers, parameters and
                // scalar operators only. Reject literals/comments rather than guessing their quoting rules.
                if (value is '\'' or '"' or ';'
                    || (value == '-' && index > 0 && sql[index - 1] == '-')
                    || (value == '*' && index > 0 && sql[index - 1] == '/'))
                {
                    throw new FormatException("The fixture payload contains unsupported literal or comment syntax.");
                }

                if (value == ')')
                {
                    depth++;
                }
                else if (value == '('
                         && --depth == 0)
                {
                    return index;
                }
            }
        }

        throw new FormatException("The fixture payload source has no balanced opening parenthesis.");
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(
        @"(?m)^SELECT\s+`(?<source>[^`]+)`\.`Id`(?:\s+AS\s+`Source`)?\s*,\s*`(?<owner>[^`]+)`\.`Id`(?:\s+AS\s+`Owner`)?(?:\s*,|\s*\r?\n)")]
    private static partial Regex ProjectionsRegex();

    [GeneratedRegex(@"\bFROM\s+`(?<table>[^`]+)`\s+AS\s+`[^`]+`")]
    private static partial Regex TablesRegex();

    [GeneratedRegex(@"\A\(\s*SELECT\b")]
    private static partial Regex SelectRegex();

    [GeneratedRegex(@"\b(?<kind>INNER\s+JOIN|STRAIGHT_JOIN)\s*\z")]
    private static partial Regex JoinRegex();
}
