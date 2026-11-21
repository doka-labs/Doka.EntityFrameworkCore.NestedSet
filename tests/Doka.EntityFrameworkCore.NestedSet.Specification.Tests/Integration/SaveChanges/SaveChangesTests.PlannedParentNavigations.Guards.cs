using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>A planned parent never authorizes callback navigation edits toward a different real parent.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveChangesAsync_PlannedParent_RejectsDifferentCallbackNavigation(
        bool collection,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using (var setup = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            var tree = setup
                .NestedSet<NavigationNode>()
                .ForScope(1);

            await tree.InsertSubtreeAsync(
                new NestedSetBranch<NavigationNode>(
                    new NavigationNode
                    {
                        Id = 4,
                        Name = "Charlie",
                    },
                    [
                        new NestedSetBranch<NavigationNode>(
                            new NavigationNode
                            {
                                Id = 5,
                                Name = "Delta",
                            }),
                        new NestedSetBranch<NavigationNode>(
                            new NavigationNode
                            {
                                Id = 6,
                                Name = "Echo",
                            }),
                    ]),
                1,
                CancellationToken.None);

            await tree.InsertChildAsync(
                new NavigationNode
                {
                    Id = 7,
                    Name = "Foxtrot",
                },
                1,
                CancellationToken.None);
        }

        var callback = new SaveBoundaryCallback();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            probe);

        var graph = await PreparePlannedParentAsync(
            context,
            "both",
            automaticDetection,
            true,
            true,
            cancellationToken: TestContext.Current.CancellationToken);

        var nodes = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Select(entry => entry.ToEntityEntry()
                .Entity)
            .OfType<NavigationNode>()
            .ToArray();

        var other = nodes.Single(node => node.Id == 4);
        var secondOther = nodes.Single(node => node.Id == 7);
        var before = CapturePlannedParentState(context);
        callback.OnSaving = _ =>
        {
            if (collection)
            {
                // WHY: Cleanup must visit both unknown targets and preserve Charlie's existing child order.
                other.Children.Add(graph.Child);
                secondOther.Children.Add(graph.Child);
            }
            else
            {
                graph.Child.Parent = other;
            }
        };

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
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
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(0, probe.Writes);
        AssertPlannedParentState(before, after);
        Assert.Equal(1, storedParent);
        Assert.Equal("original", storedNote);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
