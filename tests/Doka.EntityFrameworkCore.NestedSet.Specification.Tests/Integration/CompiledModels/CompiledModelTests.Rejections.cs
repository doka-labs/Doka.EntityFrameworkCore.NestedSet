namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class CompiledModelTests
{
    /// <summary>A valid supplied model does not replace the required native adapter registration.</summary>
    [Fact]
    public async Task ForScope_SuppliedModelWithoutAdapter_HasAnActionableDiagnostic()
    {
        // Arrange
        var sourceOptions = await _fixture.CreateOptionsAsync<LegacyCompiledContext>(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            model: null,
            enableNestedSets: true);

        await using var source = new LegacyCompiledContext(sourceOptions);
        var supplied = source.Model;
        var options = await _fixture.CreateOptionsAsync<LegacyCompiledContext>(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            model: supplied,
            enableNestedSets: false);

        var boundary = new ConfigurationIoBoundary();
        await using var context = new LegacyCompiledContext(
            new DbContextOptionsBuilder<LegacyCompiledContext>(options).AddInterceptors(boundary, boundary.Commands)
                .Options);

        // Act
        var exception = Record.Exception(() => context
            .NestedSet<TextNode>()
            .ForScope("scope"));

        // Assert
        var error = Assert.IsType<InvalidOperationException>(exception);

        Assert.Contains("matching NestedSet provider package", error.Message, StringComparison.Ordinal);
        Assert.Contains("UseNestedSets()", error.Message, StringComparison.Ordinal);
        Assert.Contains("callback", error.Message, StringComparison.Ordinal);
        Assert.Same(supplied, context.Model);
        Assert.True((bool)supplied.FindAnnotation(NestedSetAnnotationNames.CollationsCaptured)!.Value!);
        Assert.Null(
            context
                .GetService<IDbContextOptions>()
                .FindExtension<NestedSetOptionsExtension>());

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.CommandsCreated);
    }

    /// <summary>A registered runtime adapter cannot repair capture missing from a supplied model.</summary>
    [Fact]
    public async Task ForScope_SuppliedModelWithoutCollationCapture_HasAnActionableDiagnostic()
    {
        // Arrange
        var legacyOptions = await _fixture.CreateOptionsAsync<LegacyCompiledContext>(
            Engine,
            model: null,
            enableNestedSets: false,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var source = new LegacyCompiledContext(legacyOptions);
        var supplied = source.Model;

        // WHY: Register the runtime adapter independently; supplying the legacy model must skip regeneration.
        var options = await _fixture.CreateOptionsAsync<LegacyCompiledContext>(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            model: supplied,
            enableNestedSets: true);

        var boundary = new ConfigurationIoBoundary();
        await using var context = new LegacyCompiledContext(
            new DbContextOptionsBuilder<LegacyCompiledContext>(options).AddInterceptors(boundary, boundary.Commands)
                .Options);

        // Act
        var exception = Record.Exception(() => context
            .NestedSet<TextNode>()
            .ForScope("scope"));

        // Assert
        var error = Assert.IsType<InvalidOperationException>(exception);

        Assert.Contains("native provider callback's UseNestedSets()", error.Message, StringComparison.Ordinal);
        Assert.Contains("before model generation", error.Message, StringComparison.Ordinal);
        Assert.Contains("Regenerate", error.Message, StringComparison.Ordinal);
        Assert.Same(supplied, context.Model);
        Assert.Null(supplied.FindAnnotation(NestedSetAnnotationNames.CollationsCaptured));
        Assert.NotNull(
            context
                .GetService<IDbContextOptions>()
                .FindExtension<NestedSetOptionsExtension>());

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.CommandsCreated);
    }

    /// <summary>Rejects stale property-only capture with an actionable regeneration diagnostic.</summary>
    [Fact]
    public async Task Resolve_SuppliedPropertyOnlyCapture_RequiresRegeneration()
    {
        // Arrange
        var options = await _fixture.CreateOptionsAsync<StaleCollationCaptureContext>(
            Engine,
            model: null,
            enableNestedSets: false,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var source = new StaleCollationCaptureContext(options);
        var supplied = source.Model;
        var runtimeOptions = await _fixture.CreateOptionsAsync<StaleCollationCaptureContext>(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            model: supplied,
            enableNestedSets: true);

        var boundary = new ConfigurationIoBoundary();
        await using var context = new StaleCollationCaptureContext(
            new DbContextOptionsBuilder<StaleCollationCaptureContext>(runtimeOptions)
                .AddInterceptors(boundary, boundary.Commands)
                .Options);

        var owner = supplied.FindEntityType(typeof(TextNode))!;
        var key = owner.FindProperty(nameof(TextNode.Id))!;

        // Act
        var failure = Record.Exception(() => NestedSetCollations.Resolve(context, key, owner));

        // Assert
        var error = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("regenerat", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(supplied, context.Model);
        Assert.True((bool)supplied.FindAnnotation(NestedSetAnnotationNames.CollationsCaptured)!.Value!);
        Assert.Equal(
            string.Empty,
            key.FindAnnotation(NestedSetAnnotationNames.Collation)
                ?.Value);

        Assert.Null(owner.FindAnnotation(NestedSetAnnotationNames.Collation + ":" + key.Name));
        Assert.NotNull(
            context
                .GetService<IDbContextOptions>()
                .FindExtension<NestedSetOptionsExtension>());

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.CommandsCreated);
    }
}
