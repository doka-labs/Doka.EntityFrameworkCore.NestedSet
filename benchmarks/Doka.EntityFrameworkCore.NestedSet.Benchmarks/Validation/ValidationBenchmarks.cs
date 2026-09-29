namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures quick and full structural validation on independently seeded valid forests.</summary>
public class ValidationBenchmarks : DatabaseBenchmark
{
    private bool _valid;

    /// <summary>Validates every configured tree at quick detail.</summary>
    [Benchmark]
    public Task Quick() => ValidateAsync(NestedSetValidationLevel.Quick);

    /// <summary>Validates every configured tree at full adjacency detail.</summary>
    [Benchmark]
    public Task Full() => ValidateAsync(NestedSetValidationLevel.Full);

    /// <summary>Executes the selected public validation level for each independent coordinate space.</summary>
    private async Task ValidateAsync(
        NestedSetValidationLevel level
    )
    {
        _valid = true;

        foreach (var import in Fixture.Forest.Imports)
        {
            var report = await Fixture
                .Hierarchy
                .InTree(import.TreeId)
                .ValidateAsync(level, CancellationToken.None);

            _valid &= report.IsValid;
        }
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _valid = false;
        await base.PrepareAsync();
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        BenchmarkFixture.Require(_valid, "Validation did not execute successfully.");
        await base.VerifyAsync();
    }
}
