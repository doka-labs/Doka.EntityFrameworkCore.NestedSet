namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Checks workload structure independently from the optimization status of the test build.</summary>
internal static class BenchmarkValidationTestSupport
{
    /// <summary>Returns critical workload errors while leaving measurement eligibility to its own tests.</summary>
    internal static ValidationError[] StructuralErrors(BenchmarkRunInfo discovery)
    {
        var validation = new ValidationParameters(discovery.BenchmarksCases, discovery.Config);

        // WHY: Rider runs these functional tests in Debug. Only the build-optimization check is separate;
        // the real benchmark configuration still rejects unoptimized measurements, verified independently.
        return discovery.Config.GetValidators()
            .Where(validator => validator is not JitOptimizationsValidator)
            .SelectMany(validator => validator.Validate(validation))
            .Where(error => error.IsCritical)
            .ToArray();
    }
}
