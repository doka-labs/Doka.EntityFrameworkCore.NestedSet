namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Verifies that configured mutable identities remain owned across asynchronous public mutation boundaries.
/// </summary>
public abstract partial class ConfiguredMutableIdentityTests : ProviderTest
{
    private readonly ConfiguredIdentityFixture _fixture;

    /// <summary>Creates tests using isolated databases on the existing assembly-owned provider servers.</summary>
    protected ConfiguredMutableIdentityTests(
        IProviderFixture<ConfiguredIdentityFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// Caller edits cannot redirect the scope, source key, or target tree while source lookup awaits.
    /// </summary>
    [Fact]
    public async Task DetachAsTreeAsync_Detachment_OwnsConfiguredScopeNodeKeyAndTreeIdentityAcrossAwait()
    {
        // Arrange
        var barrier = new IdentityResolutionBarrier();
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            barrier);

        var scope = new MutableIdentity("scope");
        var hierarchy = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("root") },
            new MutableIdentity("source"),
            TestContext.Current.CancellationToken);

        await hierarchy.InsertChildAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("child") },
            new MutableIdentity("root"),
            TestContext.Current.CancellationToken);

        await hierarchy.InsertChildAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("other") },
            new MutableIdentity("root"),
            TestContext.Current.CancellationToken);

        var nodeKey = new MutableIdentity("child");
        var targetTree = new MutableIdentity("target");
        var operationCancellationToken = TestContext.Current.CancellationToken;
        barrier.Arm();

        // Act
        var detachment = hierarchy.DetachAsTreeAsync(nodeKey, targetTree, operationCancellationToken);
        await barrier.Attempted.Task.WaitAsync(operationCancellationToken);

        try
        {
            scope.Value = "redirected-scope";
            nodeKey.Value = "other";
            targetTree.Value = "redirected-tree";
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await detachment;
        var detached = await hierarchy
            .InTree(new MutableIdentity("target"))
            .Nodes
            .SingleAsync(operationCancellationToken);

        var retained = await hierarchy
            .InTree(new MutableIdentity("source"))
            .Nodes
            .ToArrayAsync(operationCancellationToken);

        var redirectedTreeExists = await hierarchy
            .InTree(targetTree)
            .Nodes
            .AnyAsync(operationCancellationToken);

        var redirectedScopeExists = await context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(scope)
            .TreeContaining(new MutableIdentity("child"))
            .AnyAsync(operationCancellationToken);

        // Assert
        Assert.Equal(
            ("child", "scope", "target", 1L, 2L, 0),
            (detached.Id.Value, detached.Scope.Value, detached.TreeId.Value, detached.Left, detached.Right,
                detached.Depth));

        Assert.Null(detached.ParentId);
        Assert.Collection(
            retained,
            root => Assert.Equal(("root", 1L, 4L, 0), (root.Id.Value, root.Left, root.Right, root.Depth)),
            child => Assert.Equal(
                ("other", "root", 2L, 3L, 1),
                (child.Id.Value, child.ParentId!.Value, child.Left, child.Right, child.Depth)));

        Assert.All(retained, node => Assert.Equal(("scope", "source"), (node.Scope.Value, node.TreeId.Value)));
        Assert.False(redirectedTreeExists);
        Assert.False(redirectedScopeExists);
    }

    /// <summary>Pairs every provider and insertion path with callbacks before and after payload persistence.</summary>
    public static IEnumerable<TheoryDataRow<bool, bool>> CallbackCases()
    {
        yield return new TheoryDataRow<bool, bool>(false, false);
        yield return new TheoryDataRow<bool, bool>(false, true);
        yield return new TheoryDataRow<bool, bool>(true, false);
        yield return new TheoryDataRow<bool, bool>(true, true);
    }

    /// <summary>A callback cannot mutate a configured parent snapshot shared with a staged insertion.</summary>
    /// <param name="bulk">Whether the insertion uses the branch import path.</param>
    /// <param name="afterSave">Whether the callback runs after the payload INSERT completed.</param>
    /// <returns>A task that completes after verifying exact input and database rollback.</returns>
    [Theory]
    [MemberData(nameof(CallbackCases))]
    public async Task InsertAsync_CallbackParentMutation_RestoresConfiguredInput(
        bool bulk,
        bool afterSave
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(new MutableIdentity("scope"));

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("root") },
            new MutableIdentity("source"),
            TestContext.Current.CancellationToken);

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("other") },
            new MutableIdentity("other-tree"),
            TestContext.Current.CancellationToken);

        var input = OriginalInput();
        var original = Snapshot(input);
        var rowsBefore = await RowsAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var registriesBefore = await RegistriesAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var callbacks = 0;
        ObserveSave(
            context,
            afterSave,
            () =>
            {
                callbacks++;
                input.ParentId!.Value = "other";
            });

        // Act
        var error = await Record.ExceptionAsync(() => InsertAsync(
            hierarchy,
            input,
            bulk,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            bulk
                ? NestedSetErrorCode.InvalidImport
                : NestedSetErrorCode.InvalidStructure,
            Assert.IsType<NestedSetException>(error)
                .Code);

        Assert.Equal(1, callbacks);
        Assert.Equal(original, Snapshot(input));
        Assert.Equal(rowsBefore, await RowsAsync(context, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(
            registriesBefore,
            await RegistriesAsync(
                context,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            EntityState.Detached,
            context.Entry(input)
                .State);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Replacing a parent object with the same converted identity remains a valid callback write.</summary>
    /// <param name="bulk">Whether the insertion uses the branch import path.</param>
    /// <param name="afterSave">Whether the callback runs after the payload INSERT completed.</param>
    /// <returns>A task that completes after verifying the configured parent and exact tree remain valid.</returns>
    [Theory]
    [MemberData(nameof(CallbackCases))]
    public async Task InsertAsync_Callback_MayReplaceParentWithProviderIdenticalValue(
        bool bulk,
        bool afterSave
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(new MutableIdentity("scope"));

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("root") },
            new MutableIdentity("source"),
            TestContext.Current.CancellationToken);

        var input = OriginalInput();
        MutableIdentity? assignedParent = null;
        MutableIdentity? replacementParent = null;
        ObserveSave(
            context,
            afterSave,
            () =>
            {
                assignedParent = input.ParentId;
                replacementParent = new MutableIdentity(input.ParentId!.Value);
                input.ParentId = replacementParent;
            });

        // Act
        await InsertAsync(hierarchy, input, bulk, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(assignedParent);
        Assert.NotNull(replacementParent);
        Assert.NotSame(assignedParent, replacementParent);
        Assert.Equal(
            ("input", "scope", "source", "root", 2L, 3L, 1, 0L),
            (input.Id.Value, input.Scope.Value, input.TreeId.Value, input.ParentId!.Value, input.Left, input.Right,
                input.Depth, input.Position));

        Assert.Equal(
            "root",
            (await hierarchy
                .ParentOf(new MutableIdentity("input"))
                .SingleAsync(TestContext.Current.CancellationToken)).Id.Value);

        Assert.True(
            (await hierarchy
                .InTree(new MutableIdentity("source"))
                .ValidateAsync(NestedSetValidationLevel.Full, TestContext.Current.CancellationToken)).IsValid);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Dispatches one public insertion while retaining the configured mutable parent model.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static Task InsertAsync(
        ScopedNestedSet<ConfiguredIdentityNode, MutableIdentity> hierarchy,
        ConfiguredIdentityNode input,
        bool bulk,
        CancellationToken cancellationToken
    ) => bulk
        ? hierarchy.InsertSubtreeAsync(
            new NestedSetBranch<ConfiguredIdentityNode>(input),
            new MutableIdentity("root"),
            cancellationToken)
        : hierarchy.InsertChildAsync(input, new MutableIdentity("root"), cancellationToken);
}
