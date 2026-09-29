namespace Doka.EntityFrameworkCore.NestedSet.Samples;

/// <summary>Parses the small, consistent command surface shared by the three samples.</summary>
internal sealed record SampleCommand
{
    /// <summary>Gets the database route; Doka with MariaDB is the default.</summary>
    public SampleProvider Provider { get; private init; } = SampleProvider.MariaDb;

    /// <summary>Gets the selected independent scenario, or all scenarios in their documented order.</summary>
    public string Scenario { get; private init; } = "all";

    /// <summary>Gets whether the caller requested read-only inspection of existing results.</summary>
    public bool Inspect { get; private init; }

    /// <summary>Gets whether the caller explicitly requested a reset of this sample's owned database.</summary>
    public bool Reset { get; private init; }

    /// <summary>Gets whether tree output includes structural columns and stable identities.</summary>
    public bool Details { get; private init; }

    /// <summary>Gets whether to print usage without resolving or connecting to a database.</summary>
    public bool Help { get; private init; }

    /// <summary>Validates command options before any sample can access a database.</summary>
    /// <param name="args">The arguments passed to the console application.</param>
    /// <param name="scenarios">The exact scenario names implemented by this project.</param>
    /// <returns>The validated invocation.</returns>
    /// <exception cref="ArgumentException">An option, value or combination is invalid.</exception>
    public static SampleCommand Parse(
        string[] args,
        IReadOnlyList<string> scenarios
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(scenarios);

        var command = new SampleCommand();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];

            if (!seen.Add(option))
            {
                throw new ArgumentException($"Option '{option}' was supplied more than once.", nameof(args));
            }

            command = option switch
            {
                "--provider" => command with { Provider = ParseProvider(ReadValue(args, ref index)) },
                "--scenario" => command with { Scenario = ReadValue(args, ref index) },
                "--inspect" => command with { Inspect = true },
                "--reset" => command with { Reset = true },
                "--details" => command with { Details = true },
                "--help" => command with { Help = true },
                _ => throw new ArgumentException($"Unknown option '{option}'. Use --help.", nameof(args)),
            };
        }

        if (command.Scenario != "all"
            && !scenarios.Contains(command.Scenario, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown scenario '{command.Scenario}'. Use --help.", nameof(args));
        }

        if (command.Inspect
            && (command.Reset || seen.Contains("--scenario")))
        {
            throw new ArgumentException("--inspect cannot be combined with --reset or --scenario.", nameof(args));
        }

        return command;
    }

    /// <summary>Resolves a documented provider name without accepting an arbitrary provider assembly.</summary>
    private static SampleProvider ParseProvider(
        string value
    ) => value.ToLowerInvariant() switch
    {
        "mariadb" => SampleProvider.MariaDb,
        "mysql" => SampleProvider.MySql,
        "sqlite" => SampleProvider.Sqlite,
        _ => throw new ArgumentException("Provider must be mariadb, mysql or sqlite.", nameof(value)),
    };

    /// <summary>Reads exactly one value and reports missing values before configuration is constructed.</summary>
    private static string ReadValue(
        string[] args,
        ref int index
    )
    {
        index++;

        if (index == args.Length
            || args[index].StartsWith("--", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException("--provider and --scenario require a value.", nameof(args));
        }

        return args[index];
    }
}
