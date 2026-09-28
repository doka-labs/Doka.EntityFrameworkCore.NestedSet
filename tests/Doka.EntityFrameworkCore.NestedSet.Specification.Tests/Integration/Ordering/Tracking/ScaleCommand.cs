namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Holds measured provider shape and reader activity for one completed command.</summary>
internal sealed class ScaleCommand
{
    /// <summary>Captures scalar command evidence before EF disposes its provider command.</summary>
    internal ScaleCommand(
        string sql,
        int parameters
    )
    {
        Sql = sql;
        Parameters = parameters;
    }

    /// <summary>Gets the executed SQL, including its actual provider collection translation.</summary>
    internal string Sql { get; }

    /// <summary>Gets the physical provider parameter count.</summary>
    internal int Parameters { get; }

    /// <summary>Gets reader calls, including the final exhausted read when applicable.</summary>
    internal int ReadCalls { get; set; }
}
