namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Defines explicit dataset settings shared by the launcher and its generated benchmark processes.</summary>
public sealed record BenchmarkRunOptions
{
    private const string OptionsVariable = "NESTEDSET_BENCHMARK_OPTIONS";

    /// <summary>Gets the selected engine; SQLite in memory is the local default.</summary>
    public BenchmarkEngine Engine { get; init; } = BenchmarkEngine.SqliteMemory;

    /// <summary>Gets the selected node counts.</summary>
    public int[] NodeCounts { get; init; } = [1000];

    /// <summary>Gets the selected deterministic tree shapes.</summary>
    public BenchmarkShape[] Shapes { get; init; } = [BenchmarkShape.Balanced];

    /// <summary>Gets the selected independent tree counts.</summary>
    public int[] TreeCounts { get; init; } = [1];

    /// <summary>Gets the selected change tracker populations.</summary>
    public int[] TrackedCounts { get; init; } = [0];

    /// <summary>Gets the absolute directory containing this run's exports and provenance.</summary>
    public string ArtifactsPath { get; init; } = CreateArtifactsPath();

    /// <summary>Gets whether to run untimed SQL and write diagnostics.</summary>
    public bool Diagnostics { get; init; }

    /// <summary>Gets arguments forwarded unchanged to BenchmarkDotNet.</summary>
    public string[] Arguments { get; init; } = [];

    /// <summary>Gets settings from the inherited environment in the generated benchmark process.</summary>
    public static BenchmarkRunOptions Current
    {
        get
        {
            var serialized = Environment.GetEnvironmentVariable(OptionsVariable);

            if (string.IsNullOrEmpty(serialized))
            {
                return new BenchmarkRunOptions();
            }

            var options = JsonSerializer.Deserialize<BenchmarkRunOptions>(serialized)
                ?? throw new InvalidOperationException("The inherited benchmark settings are missing.");

            options.Validate();

            return options;
        }
    }

    /// <summary>Parses harness settings while preserving BenchmarkDotNet command line arguments.</summary>
    /// <param name="arguments">The launcher command line.</param>
    /// <returns>Validated settings and the remaining BenchmarkDotNet arguments.</returns>
    /// <exception cref="ArgumentException">A harness setting is missing or invalid.</exception>
    public static BenchmarkRunOptions Parse(
        string[] arguments
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var options = new BenchmarkRunOptions();
        var forwarded = new List<string>();

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            var equals = argument.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? argument : argument[..equals];

            if (name == "--diagnostics")
            {
                if (equals >= 0)
                {
                    throw new ArgumentException("--diagnostics is a flag and does not accept a value.");
                }

                options = options with { Diagnostics = true };
                continue;
            }

            if (name is not ("--engine" or "--nodes" or "--shape" or "--trees" or "--tracked" or "--artifacts"))
            {
                forwarded.Add(argument);
                continue;
            }

            var value = equals >= 0 ? argument[(equals + 1)..] : ReadValue(arguments, ref index, name);
            options = name switch
            {
                "--engine" => options with { Engine = ParseEnum<BenchmarkEngine>(value, name) },
                "--nodes" => options with { NodeCounts = ParseCounts(value, name, 10) },
                "--shape" => options with { Shapes = ParseShapes(value) },
                "--trees" => options with { TreeCounts = ParseCounts(value, name, 1) },
                "--tracked" => options with { TrackedCounts = ParseCounts(value, name, 0) },
                "--artifacts" => options with { ArtifactsPath = value },
                _ => throw new InvalidOperationException("An unrecognized harness setting reached the parser."),
            };
        }

        options = options with { Arguments = forwarded.ToArray() };
        options.Validate();

        return options;
    }

    /// <summary>Publishes validated settings before BenchmarkDotNet launches generated child processes.</summary>
    public void ApplyEnvironment()
    {
        Validate();
        Environment.SetEnvironmentVariable(OptionsVariable, JsonSerializer.Serialize(this));
    }

    /// <summary>Checks primitive settings before native selection validates the chosen scenario combinations.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(Engine)
            || NodeCounts is not { Length: > 0 }
            || NodeCounts.Any(count => count < 10)
            || Shapes is not { Length: > 0 }
            || Shapes.Any(shape => !Enum.IsDefined(shape))
            || TreeCounts is not { Length: > 0 }
            || TreeCounts.Any(count => count < 1)
            || TrackedCounts is not { Length: > 0 }
            || TrackedCounts.Any(count => count < 0))
        {
            throw new ArgumentException("Benchmark settings require nodes >= 10, trees >= 1 and tracked >= 0.");
        }

        if (string.IsNullOrWhiteSpace(ArtifactsPath)
            || !Path.IsPathFullyQualified(ArtifactsPath))
        {
            throw new ArgumentException("--artifacts must identify an absolute run directory.");
        }
    }

    /// <summary>Consumes a required separate value without swallowing the next harness option.</summary>
    /// <param name="arguments">The original launcher arguments.</param>
    /// <param name="index">The option position, advanced to the consumed value.</param>
    /// <param name="name">The option name used in an invalid-setting diagnostic.</param>
    /// <returns>The unmodified option value.</returns>
    private static string ReadValue(
        string[] arguments,
        ref int index,
        string name
    )
    {
        if (index + 1 >= arguments.Length
            || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} requires a value.");
        }

        index++;

        return arguments[index];
    }

    /// <summary>Validates dataset populations and removes repeated cases while retaining selection order.</summary>
    /// <param name="value">Comma-separated population values.</param>
    /// <param name="name">The option name used in an invalid-setting diagnostic.</param>
    /// <param name="minimum">The lowest meaningful population for this option.</param>
    /// <returns>The distinct validated populations.</returns>
    private static int[] ParseCounts(
        string value,
        string name,
        int minimum
    )
    {
        var counts = new List<int>();

        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || count < minimum)
            {
                throw new ArgumentException($"{name} requires comma-separated integers >= {minimum}.");
            }

            counts.Add(count);
        }

        return counts
            .Distinct()
            .ToArray();
    }

    /// <summary>Validates named shapes and preserves the user's distinct selection order.</summary>
    /// <param name="value">Comma-separated named shapes.</param>
    /// <returns>The distinct validated shapes.</returns>
    private static BenchmarkShape[] ParseShapes(
        string value
    ) => value
        .Split(',', StringSplitOptions.TrimEntries)
        .Select(part => ParseEnum<BenchmarkShape>(part, "--shape"))
        .Distinct()
        .ToArray();

    /// <summary>Accepts declared names case-insensitively while rejecting numeric enum representations.</summary>
    /// <typeparam name="TEnum">The engine or shape selection enum.</typeparam>
    /// <param name="value">The user-supplied name.</param>
    /// <param name="name">The option name used in an invalid-setting diagnostic.</param>
    /// <returns>The declared enum value.</returns>
    private static TEnum ParseEnum<TEnum>(
        string value,
        string name
    )
        where TEnum : struct, Enum
    {
        if (!Enum
                .GetNames<TEnum>()
                .Contains(value, StringComparer.OrdinalIgnoreCase)
            || !Enum.TryParse<TEnum>(value, true, out var parsed))
        {
            throw new ArgumentException($"{name} requires one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        }

        return parsed;
    }

    /// <summary>Gives independent runs unique directories beneath the repository's ignored artifacts tree.</summary>
    /// <returns>An absolute run directory.</returns>
    private static string CreateArtifactsPath() => Path.GetFullPath(
        Path.Combine("artifacts", "benchmarks", $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"));
}
