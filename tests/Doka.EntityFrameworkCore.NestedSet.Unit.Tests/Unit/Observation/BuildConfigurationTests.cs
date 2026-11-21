namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks build boundaries through the pinned SDK without compiling or restoring packages.</summary>
public sealed class BuildConfigurationTests
{
    /// <summary>Repository defaults ignore ambient outputs while explicit SDK overrides retain ownership.</summary>
    /// <param name="environmentOverride">Whether an inherited environment value conflicts with the default.</param>
    /// <param name="globalOverride">Whether the caller explicitly owns the output path.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ArtifactsPath_AmbientAndExplicitInputs_RespectsOnlyTheExplicitOverride(
        bool environmentOverride,
        bool globalOverride
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var probe = await BuildConfigurationProbe.CreateAsync(cancellationToken);
        var ambient = environmentOverride ? Path.Combine(probe.ProjectDirectory, "ambient") : null;
        var requested = globalOverride ? Path.Combine(probe.ProjectDirectory, "explicit") : null;

        // Act
        using var result = await probe.EvaluateArtifactsAsync(ambient, requested, cancellationToken);

        // Assert
        var properties = result.RootElement.GetProperty("Properties");
        var projectDirectory = Assert.IsType<string>(properties.GetProperty("MSBuildProjectDirectory").GetString());
        var expected = requested ?? Path.Combine(projectDirectory, "artifacts");
        var actual = properties.GetProperty("ArtifactsPath").GetString();
        Assert.Equal(expected, Path.GetFullPath(Assert.IsType<string>(actual)));
    }

    /// <summary>Stale generated sources never become inputs while ordinary source remains included.</summary>
    /// <param name="directory">A generated-output location excluded from the source glob.</param>
    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("BenchmarkDotNet.Artifacts/Generated")]
    public async Task CompileInputs_StaleGeneratedOutput_ExcludesOnlyGeneratedSources(
        string directory
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var probe = await BuildConfigurationProbe.CreateAsync(cancellationToken);
        await probe.WriteProjectFileAsync("Included.cs", "internal sealed class Included { }", cancellationToken);
        await probe.WriteProjectFileAsync(
            directory + "/Stale.cs",
            "This generated output must never become a Compile item.",
            cancellationToken);

        // Act
        using var result = await probe.EvaluateAsync(["-getItem:Compile"], cancellationToken);

        // Assert
        var source = Assert.Single(result.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray());
        Assert.Equal("Included.cs", source.GetProperty("Identity").GetString());
    }

    /// <summary>Only the explicit audit run treats advisory or audit-service warnings as errors.</summary>
    /// <param name="strict">Whether the caller explicitly requests strict auditing.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NuGetAudit_ExplicitStrictMode_PromotesOnlyTheSelectedAuditPath(
        bool strict
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var probe = await BuildConfigurationProbe.CreateAsync(cancellationToken);
        string[] arguments =
        [
            "-getProperty:WarningsAsErrors,WarningsNotAsErrors",
            "-p:DokaAuditPipeline=" + (strict ? "true" : "false"),
        ];

        // Act
        using var result = await probe.EvaluateAsync(arguments, cancellationToken);

        // Assert
        var properties = result.RootElement.GetProperty("Properties");
        var errors = properties.GetProperty("WarningsAsErrors").GetString()!.Split(';');
        var warnings = properties.GetProperty("WarningsNotAsErrors").GetString()!.Split(';');
        string[] codes = ["NU1900", "NU1901", "NU1902", "NU1903", "NU1904", "NU1905"];

        foreach (var code in codes)
        {
            Assert.Equal(strict, errors.Contains(code));
            Assert.Equal(!strict, warnings.Contains(code));
        }
    }

    /// <summary>CI locks platform-neutral graphs while local edits and RID-specific restores remain explicit.</summary>
    /// <param name="continuousIntegration">Whether this is a CI evaluation.</param>
    /// <param name="runtimeIdentifier">The requested runtime, or an empty platform-neutral value.</param>
    /// <param name="locked">Whether this context must reject dependency drift.</param>
    [Theory]
    [InlineData(false, "", false)]
    [InlineData(true, "", true)]
    [InlineData(true, "linux-x64", false)]
    public async Task RestoreLocks_LocalCiAndRuntimeContexts_ApplyTheDeclaredLockBoundary(
        bool continuousIntegration,
        string runtimeIdentifier,
        bool locked
    )
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var probe = await BuildConfigurationProbe.CreateAsync(cancellationToken);
        string[] arguments =
        [
            "-getProperty:RestorePackagesWithLockFile,RestoreLockedMode",
            "-p:ContinuousIntegrationBuild=" + (continuousIntegration ? "true" : "false"),
            "-p:RuntimeIdentifier=" + runtimeIdentifier,
        ];

        // Act
        using var result = await probe.EvaluateAsync(arguments, cancellationToken);

        // Assert
        var properties = result.RootElement.GetProperty("Properties");
        Assert.Equal("true", properties.GetProperty("RestorePackagesWithLockFile").GetString());
        Assert.Equal(locked, properties.GetProperty("RestoreLockedMode").GetString() == "true");
    }

    /// <summary>Qualified builds redirect this nonshipping graph to its owned lock copy.</summary>
    [Fact]
    public async Task RestoreLocks_QualificationRoot_RedirectsTheNonshippingProjectLock()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var probe = await BuildConfigurationProbe.CreateAsync(cancellationToken);
        var root = Path.Combine(probe.ProjectDirectory, "owned-locks");

        // Act
        using var result = await probe.EvaluateAsync(
            ["-getProperty:NuGetLockFilePath", "-p:NestedSetReleaseLockRoot=" + root, "-getItem:Compile"],
            cancellationToken);

        // Assert
        var actual = result.RootElement.GetProperty("Properties").GetProperty("NuGetLockFilePath").GetString();
        Assert.NotNull(actual);
        Assert.Equal(
            Path.Combine(root, "Boundary", "packages.lock.json"),
            Path.GetFullPath(actual));
    }

    /// <summary>A machine or parent feed cannot become an implicit restore source.</summary>
    [Fact]
    public async Task RestoreSources_UnrelatedInheritedFeed_UsesOnlyTheRepositoryFeed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var probe = await BuildConfigurationProbe.CreateAsync(cancellationToken);
        await probe.WriteParentSourceAsync(cancellationToken);

        // Act
        var result = await probe.ListSourcesAsync(cancellationToken);

        // Assert
        Assert.Equal("E https://api.nuget.org/v3/index.json", result.Trim());
    }

    /// <summary>A failed native cleanup step does not prevent observing either failed stream drain.</summary>
    [Fact]
    public async Task BuildProbeCleanup_UnassociatedProcessAndFailedReaders_RetainsAllFailures()
    {
        // Arrange
        using var process = new Process();
        var outputFailure = new IOException("Injected output drain failure.");
        var errorFailure = new IOException("Injected error drain failure.");
        var output = Task.FromException<string>(outputFailure);
        var error = Task.FromException<string>(errorFailure);

        // WHY: Process has no overridable termination seam. Exercise the existing private cleanup boundary
        // without exposing test-only APIs or starting an unowned child merely to provoke an invalid association.
        var method = typeof(BuildConfigurationProbe).GetMethod(
            "CompleteProcessAsync",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        var cleanup = (Task)method.Invoke(null, [process, output, error])!;
        var failure = await Record.ExceptionAsync(() => cleanup);

        // Assert
        var aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Contains(outputFailure, aggregate.InnerExceptions);
        Assert.Contains(errorFailure, aggregate.InnerExceptions);
        Assert.Equal(2, aggregate.InnerExceptions.Count(exception => exception is InvalidOperationException));
    }
}
