namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures quick and full structural validation on independently seeded valid forests.</summary>
public class ValidationBenchmarks : DatabaseBenchmark
{
    private bool _valid;

    /// <summary>Validates every configured tree at quick detail.</summary>
    [Benchmark]
    public Task Quick() => ValidateAsync(NestedSetValidationLevel.Quick, CancellationToken.None);

    /// <summary>Validates every configured tree at full adjacency detail.</summary>
    [Benchmark]
    public Task Full() => ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

    /// <summary>Executes the selected public validation level for each independent coordinate space.</summary>
    /// <param name="level">The public structural validation level to execute.</param>
    /// <param name="cancellationToken">Cancellation for the operation's preparation or verification work.</param>
    private async Task ValidateAsync(
        NestedSetValidationLevel level,
        CancellationToken cancellationToken
    )
    {
        _valid = true;

        foreach (var import in Fixture.Forest.Imports)
        {
            var report = await Fixture
                .Hierarchy
                .InTree(import.TreeId)
                .ValidateAsync(level, cancellationToken);

            _valid &= report.IsValid;
        }
    }

    /// <inheritdoc />
    public override async Task PrepareAsync(
        CancellationToken cancellationToken
    )
    {
        _valid = false;
        await base.PrepareAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task VerifyAsync(
        CancellationToken cancellationToken
    )
    {
        BenchmarkFixture.Require(_valid, "Validation did not execute successfully.");
        await base.VerifyAsync(cancellationToken);
    }
}
