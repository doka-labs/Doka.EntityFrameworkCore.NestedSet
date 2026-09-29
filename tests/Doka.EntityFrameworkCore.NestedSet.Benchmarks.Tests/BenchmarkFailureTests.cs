namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies that failed observations remain diagnosable without exposing provider secrets.</summary>
public sealed class BenchmarkFailureTests
{
    /// <summary>Error types, stable codes and source frames survive ordinary and aggregate failure reporting.</summary>
    /// <param name="aggregate">Whether startup and cleanup both failed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureDiagnosticsRetainEvidenceAndExcludeSecretPayload(
        bool aggregate
    )
    {
        // Arrange
        const string secret = "Password=adversarial-benchmark-secret";
        var provider = CaptureThrown(new IOException(secret));
        var hierarchy = CaptureThrown(new NestedSetException(NestedSetErrorCode.InvalidStructure, secret, provider));
        var error = aggregate
            ? CaptureThrown(new AggregateException(secret, hierarchy, CaptureThrown(new ArgumentException(secret))))
            : hierarchy;

        error.Data["connection"] = secret;

        // Act
        var diagnostic = Program.DescribeFailure(error);

        // Assert
        Assert.Contains(typeof(NestedSetException).FullName!, diagnostic, StringComparison.Ordinal);
        Assert.Contains(nameof(NestedSetErrorCode.InvalidStructure), diagnostic, StringComparison.Ordinal);
        Assert.Contains(typeof(IOException).FullName!, diagnostic, StringComparison.Ordinal);
        Assert.Contains(nameof(CaptureThrown), diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("connection", diagnostic, StringComparison.Ordinal);
        if (aggregate)
        {
            Assert.Contains(typeof(AggregateException).FullName!, diagnostic, StringComparison.Ordinal);
            Assert.Contains(typeof(ArgumentException).FullName!, diagnostic, StringComparison.Ordinal);
        }
    }

    /// <summary>Gives a synthetic provider failure a real origin frame for diagnostic verification.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Exception CaptureThrown(
        Exception exception
    )
    {
        try
        {
            throw exception;
        }
        catch (Exception captured)
        {
            return captured;
        }
    }
}
