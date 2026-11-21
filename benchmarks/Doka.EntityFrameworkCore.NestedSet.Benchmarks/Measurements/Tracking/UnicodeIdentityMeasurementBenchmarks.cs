namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes scalar counting across ASCII, BMP, supplementary and mixed Unicode inputs.</summary>
public class UnicodeIdentityMeasurementBenchmarks
{
    /// <summary>Gets or sets an eight-scalar input with its original Unicode representation.</summary>
    [Params(
        "abcdefgh", "\u00e4\u03a9\u4e2d\u00f1\u0416\u00e9\u3042\u20ac",
        "\U0001f600\U0001f601\U0001f602\U0001f603\U0001f604\U0001f605\U0001f606\U0001f607",
        "ab\U0001f600c\U0001f601def")]
    public string Input { get; set; } = "abcdefgh";

    /// <summary>Counts one identity using the production validator.</summary>
    /// <returns>The complete number of Unicode scalar values.</returns>
    [Benchmark]
    public int CountScalars() => NestedSetIdentityLength.CountUnicodeScalars(Input, "TreeId");

    /// <summary>Verifies each input's semantic result outside native measurement.</summary>
    [GlobalSetup]
    [GlobalCleanup]
    public void VerifyWorkload()
    {
        if (CountScalars() != 8)
        {
            throw new InvalidOperationException("The Unicode identity workload must contain eight scalars.");
        }
    }
}
