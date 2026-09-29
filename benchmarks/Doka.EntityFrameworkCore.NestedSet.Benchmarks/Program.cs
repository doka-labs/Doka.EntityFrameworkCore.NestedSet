namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Runs explicitly selected observational benchmarks or untimed database diagnostics.</summary>
public static class Program
{
    /// <summary>Parses settings, owns the database lifecycle, and delegates measurement to BenchmarkDotNet.</summary>
    /// <param name="args">Harness settings followed by ordinary BenchmarkDotNet arguments.</param>
    /// <returns>Zero for valid observations; nonzero for invalid settings, execution or scenario results.</returns>
    public static async Task<int> Main(
        string[] args
    )
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        try
        {
            BenchmarkRunOptions options;
            try
            {
                options = BenchmarkRunOptions.Parse(args);
            }
            catch (ArgumentException exception)
            {
                await Console.Error.WriteLineAsync(exception.Message.AsMemory(), CancellationToken.None);

                return 2;
            }

            options.ApplyEnvironment();
            var config = BenchmarkConfiguration.Create(options);
            var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);

            if (args.Length == 0)
            {
                await WriteUsageAsync(CancellationToken.None);
                switcher.Run(["--list", "flat"], config);

                return 0;
            }

            var help = options.Arguments.Contains("--help", StringComparer.OrdinalIgnoreCase);

            if (help)
            {
                await WriteUsageAsync(CancellationToken.None);
            }

            var parsed = ConfigParser.Parse(options.Arguments, ConsoleLogger.Default, config);

            if (!parsed.isSuccess)
            {
                return help ? 0 : 2;
            }

            options = options with
            {
                ArtifactsPath = Path.GetFullPath(
                    parsed.options.ArtifactsDirectory?.FullName
                    ?? parsed.config.ArtifactsPath ?? options.ArtifactsPath),
            };

            config = BenchmarkConfiguration.Create(options);
            options.ApplyEnvironment();
            var effectiveConfig = ManualConfig.Union(config, parsed.config);

            if (parsed.options.PrintInformation
                || parsed.options.ListBenchmarkCaseMode != ListBenchmarkCaseMode.Disabled)
            {
                switcher.Run(options.Arguments, config);

                return 0;
            }

            if (!BenchmarkSelection.HasExplicitSelection(parsed.options))
            {
                // WHY: BDN's interactive prompt is internal and selects cases after resource preflight would run.
                await WriteUsageAsync(CancellationToken.None);
                switcher.Run(["--list", "flat"], effectiveConfig);
                await Console.Error.WriteLineAsync(
                    ("Select benchmarks with --filter, --allCategories, --anyCategories or --attribute; "
                        + "use --filter '*' to select every benchmark.").AsMemory(),
                    CancellationToken.None);

                return 2;
            }

            BenchmarkSelection selected;

            try
            {
                selected = BenchmarkSelection.Discover(effectiveConfig, options.Diagnostics);
            }
            catch (ArgumentException exception)
            {
                await Console.Error.WriteLineAsync(exception.Message.AsMemory(), CancellationToken.None);

                return 2;
            }

            using (selected)
            {
#if DEBUG
                // WHY: Timing needs optimized code; untimed command diagnostics must remain debuggable in Rider.
                if (!options.Diagnostics)
                {
                    await Console.Error.WriteLineAsync(
                        "Benchmark measurements require dotnet run -c Release.".AsMemory(),
                        CancellationToken.None);

                    return 2;
                }

#endif
                var runStartedAtUtc = DateTimeOffset.UtcNow;
                await using var environment = selected.RequiresDatabase
                    ? await BenchmarkEnvironment.CreateAsync(options, CancellationToken.None)
                    : null;

                Directory.CreateDirectory(options.ArtifactsPath);
                await BenchmarkProvenance.WriteAsync(
                    options,
                    environment,
                    runStartedAtUtc,
                    [],
                    "started",
                    CancellationToken.None);

                var summaries = Array.Empty<Summary>();

                try
                {
                    if (options.Diagnostics)
                    {
                        await BenchmarkDiagnostics.RunAsync(options, selected, CancellationToken.None);
                        await BenchmarkProvenance.WriteAsync(
                            options,
                            environment,
                            runStartedAtUtc,
                            [],
                            "completed",
                            CancellationToken.None);

                        return 0;
                    }

                    // WHY: The native switcher owns ApplesToApples handling, child execution and framework reporting.
                    summaries = switcher.Run(options.Arguments, config).ToArray();
                    var valid = summaries.Length > 0
                        && summaries.All(summary => !summary.HasCriticalValidationErrors
                            && summary.Reports.Length > 0
                            && summary.Reports.All(report => report.Success && report.ResultStatistics is not null));

                    await BenchmarkProvenance.WriteAsync(
                        options,
                        environment,
                        runStartedAtUtc,
                        summaries,
                        valid ? "completed" : "invalid",
                        CancellationToken.None);

                    return valid ? 0 : 1;
                }
                catch
                {
                    await BenchmarkProvenance.WriteAsync(
                        options,
                        environment,
                        runStartedAtUtc,
                        summaries,
                        "failed",
                        CancellationToken.None);

                    throw;
                }
            }
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(
                DescribeFailure(exception)
                    .AsMemory(),
                CancellationToken.None);

            return 1;
        }
    }

    /// <summary>Reports error codes and source locations without provider messages or connection data.</summary>
    /// <param name="exception">The failed startup, framework execution or observation.</param>
    /// <returns>Exception categories, NestedSet codes and stack frames suitable for launcher diagnostics.</returns>
    internal static string DescribeFailure(
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        var details = new StringBuilder("Benchmark execution failed.");
        AppendFailure(details, exception);

        return details.ToString();
    }

    /// <summary>Walks original and cleanup failures using safe metadata instead of exception messages.</summary>
    /// <param name="details">The diagnostic output under construction.</param>
    /// <param name="exception">The current failure in the exception hierarchy.</param>
    private static void AppendFailure(
        StringBuilder details,
        Exception exception
    )
    {
        details.AppendLine();
        details.Append(
            exception.GetType()
                .FullName);

        if (exception is NestedSetException nestedSet)
        {
            details.Append("; code=");
            details.Append(nestedSet.Code);
        }

        if (exception.StackTrace is not null)
        {
            details.AppendLine();
            details.Append(exception.StackTrace);
        }

        // WHY: Provider messages can contain credentials; stack frames and typed codes retain the actionable context.
        if (exception is AggregateException aggregate)
        {
            foreach (var failure in aggregate.InnerExceptions)
            {
                AppendFailure(details, failure);
            }
        }
        else if (exception.InnerException is not null)
        {
            AppendFailure(details, exception.InnerException);
        }
    }

    /// <summary>Lists harness settings before framework help or discovery without provisioning a database.</summary>
    /// <param name="cancellationToken">Cancellation for console output.</param>
    /// <returns>A task that completes when the harness usage text has been written.</returns>
    private static Task WriteUsageAsync(
        CancellationToken cancellationToken
    ) => Console.Out.WriteLineAsync(
        """
            NestedSet observational benchmarks (measurements require Release)
              --engine MySql|MariaDb|PostgreSql|SqlServer|SqliteMemory|SqliteFile
              --nodes 100,1000,10000,100000  (default 1000; minimum 10)
              --shape Wide,Deep,Balanced    (default Balanced)
              --trees 1,4                  (default 1; minimum 1)
              --tracked 0,1000             (default 0; minimum 0)
              --artifacts /absolute/run-directory
              --diagnostics                (untimed SQL and write observations; Debug supported)
            Other arguments are handled by BenchmarkDotNet, for example:
              --filter '*Insert*' --job short
              --list flat
            Container resources: 2 CPUs and 2 GiB. Results do not impose performance thresholds.
            """.AsMemory(),
        cancellationToken);
}
