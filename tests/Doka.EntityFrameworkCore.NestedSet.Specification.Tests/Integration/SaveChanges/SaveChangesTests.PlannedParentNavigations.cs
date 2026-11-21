namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Accepts explicitly marked parents alongside the application's own navigation synchronization.</summary>
    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("collections", false)]
    [InlineData("collections", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task SaveChangesAsync_MarkedParentWithSynchronizedNavigations_Commits(
        string navigation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var graph = await PreparePlannedParentAsync(
            context,
            navigation,
            automaticDetection,
            false,
            false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedParent = await verification
            .Set<NavigationNode>()
            .Where(node => node.Id == 2)
            .Select(node => node.ParentId)
            .SingleAsync(CancellationToken.None);

        var validation = await verification
            .NestedSet<NavigationNode>()
            .ForScope(graph.Child.Scope)
            .InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal(3, storedParent);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Callback failure restores current, original, navigation and relationship state exactly.</summary>
    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("collections", false)]
    [InlineData("collections", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task SaveChangesAsync_FailedMarkedParentMove_RestoresNavigationSnapshots(
        string navigation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = new PlannedParentFailure();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        var graph = await PreparePlannedParentAsync(
            context,
            navigation,
            automaticDetection,
            true,
            true,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedParent = await verification
            .Set<NavigationNode>()
            .Where(node => node.Id == 2)
            .Select(node => node.ParentId)
            .SingleAsync(CancellationToken.None);

        var storedNote = await verification
            .Set<NavigationNote>()
            .Select(note => note.Value)
            .SingleAsync(CancellationToken.None);

        var validation = await verification
            .NestedSet<NavigationNode>()
            .ForScope(graph.Child.Scope)
            .InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, error);
        AssertPlannedParentState(before, after);
        Assert.Equal(1, storedParent);
        Assert.Equal("original", storedNote);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>
    ///     The identical retry succeeds without repairing any graph, scalar, flag or relationship snapshot.
    /// </summary>
    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("collections", false)]
    [InlineData("collections", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task SaveChangesAsync_MarkedParentMove_RetriesIdenticallyAfterCallbackFailure(
        string navigation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = new PlannedParentFailure();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        var graph = await PreparePlannedParentAsync(
            context,
            navigation,
            automaticDetection,
            true,
            true,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);

        // WHY: The failed boundary is this retry test's precondition; its observer fails only once.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var afterFailure = CapturePlannedParentState(context);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedParent = await verification
            .Set<NavigationNode>()
            .Where(node => node.Id == 2)
            .Select(node => node.ParentId)
            .SingleAsync(CancellationToken.None);

        var storedNote = await verification
            .Set<NavigationNote>()
            .Select(note => note.Value)
            .SingleAsync(CancellationToken.None);

        var validation = await verification
            .NestedSet<NavigationNode>()
            .ForScope(graph.Child.Scope)
            .InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, firstError);
        AssertPlannedParentState(before, afterFailure);
        Assert.Equal(3, storedParent);
        Assert.Equal("planned payload", storedNote);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A committed marked parent move leaves the same context usable for ordinary application work.</summary>
    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("collections", false)]
    [InlineData("collections", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task SaveChangesAsync_MarkedParentMove_AllowsFollowingOrdinarySave(
        string navigation,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var graph = await PreparePlannedParentAsync(
            context,
            navigation,
            automaticDetection,
            false,
            false,
            cancellationToken: TestContext.Current.CancellationToken);

        await context.SaveChangesAsync(CancellationToken.None);

        // WHY: Reuse the captured property entry without an incidental EntityEntry-local detection pass.
        graph.NoteValue.CurrentValue = "following payload";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedParent = await verification
            .Set<NavigationNode>()
            .Where(node => node.Id == 2)
            .Select(node => node.ParentId)
            .SingleAsync(CancellationToken.None);

        var storedNote = await verification
            .Set<NavigationNote>()
            .Select(note => note.Value)
            .SingleAsync(CancellationToken.None);

        var validation = await verification
            .NestedSet<NavigationNode>()
            .ForScope(graph.Child.Scope)
            .InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal(3, storedParent);
        Assert.Equal("following payload", storedNote);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
