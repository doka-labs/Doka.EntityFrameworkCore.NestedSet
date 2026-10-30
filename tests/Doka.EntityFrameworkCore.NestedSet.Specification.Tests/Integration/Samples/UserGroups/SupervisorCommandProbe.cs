using System.Text.Json;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes the real sample reader and rejects hidden synchronous execution.</summary>
internal sealed class SupervisorCommandProbe : DbCommandInterceptor, IMaterializationInterceptor
{
    /// <summary>Gets or sets whether setup has ended and the consumer operation is being observed.</summary>
    internal bool Enabled { get; set; }

    /// <summary>Gets the measured reader commands.</summary>
    internal List<string> Commands { get; } = [];

    /// <summary>Gets actual parameter counts for measured reader commands.</summary>
    internal List<int> ParameterCounts { get; } = [];

    /// <summary>Gets synthetic fixture parameter values for verifying real command parameterization.</summary>
    internal List<object?> ParameterValues { get; } = [];

    /// <summary>Gets reader advances, including the final end-of-stream probe.</summary>
    internal int ReaderAdvances { get; private set; }

    /// <summary>Gets the materialized sample hierarchy entities.</summary>
    internal int Entities { get; private set; }

    /// <summary>Gets or sets an optional asynchronous barrier before a measured reader executes.</summary>
    internal Func<int, CancellationToken, Task>? BeforeReadAsync { get; set; }

    /// <summary>Finds an exact scalar string or a string element in a native or JSON array parameter.</summary>
    /// <param name="expected">The complete string value whose transport is being asserted.</param>
    /// <returns>Whether the original parameter values contain this exact value.</returns>
    internal bool HasStringParameterElement(string expected)
        => ParameterValues.Any(value => value switch
        {
            string text when string.Equals(text, expected, StringComparison.Ordinal) => true,
            string[] elements => elements.Contains(expected, StringComparer.Ordinal),
            string json when json.StartsWith('[') => HasJsonStringElement(json, expected),
            _ => false,
        });

    /// <summary>Checks structured array elements without accepting substrings or changing captured values.</summary>
    private static bool HasJsonStringElement(string json, string expected)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.EnumerateArray().Any(element => element.ValueKind == JsonValueKind.String
                    && string.Equals(element.GetString(), expected, StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            // WHY: Other scalar parameters may begin with '[' without being a provider collection parameter.
            return false;
        }
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        if (Enabled)
        {
            throw new InvalidOperationException("The supervisor sample must execute readers asynchronously.");
        }

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        if (Enabled)
        {
            Commands.Add(command.CommandText);
            ParameterCounts.Add(command.Parameters.Count);
            ParameterValues.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value));
            if (BeforeReadAsync is { } barrier)
            {
                await barrier(Commands.Count, cancellationToken);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult DataReaderDisposing(
        DbCommand command,
        DataReaderDisposingEventData eventData,
        InterceptionResult result
    )
    {
        if (Enabled)
        {
            ReaderAdvances += eventData.ReadCount;
        }

        return result;
    }

    /// <inheritdoc />
    public object InitializedInstance(
        MaterializationInterceptionData materializationData,
        object entity
    )
    {
        if (Enabled
            && entity is App.User or App.UserGroup)
        {
            Entities++;
        }

        return entity;
    }
}
