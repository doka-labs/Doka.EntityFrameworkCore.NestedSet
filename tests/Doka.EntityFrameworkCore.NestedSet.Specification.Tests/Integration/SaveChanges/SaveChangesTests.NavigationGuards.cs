using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Ignoring detached snapshot members must not permit late removal of a still-tracked child.</summary>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public async Task LateTrackedChildRemovalIsRejectedBeforeWrites(
        bool wrapped,
        bool automaticDetection,
        bool synchronous,
        bool explicitDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped, probe);
        var root = await context.Set<NavigationNode>().Include(node => node.Children)
            .SingleAsync(node => node.Id == 1, CancellationToken.None);

        var child = Assert.Single(root.Children, node => node.Id == 2);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        await context.AddAsync(new NavigationNote { Id = 2 }, CancellationToken.None);
        context.SavingChanges += (_, _) =>
        {
            // WHY: A tracked removal can change ParentId; only detached snapshot remnants are irrelevant.
            root.Children.Remove(child);

            if (explicitDetection)
            {
                context.ChangeTracker.DetectChanges();
            }
        };

        // Act
        var failure = await Record.ExceptionAsync(() => SaveNavigationAsync(context, synchronous));
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var storedParent = await verification.Set<NavigationNode>().Where(node => node.Id == 2)
            .Select(node => node.ParentId).SingleAsync(CancellationToken.None);

        var storedNodes = await verification.Set<NavigationNode>().CountAsync(CancellationToken.None);
        var storedNotes = await verification.Set<NavigationNote>().CountAsync(CancellationToken.None);
        var remainsTracked = context.GetService<IUpdateAdapterFactory>().Create().Entries
            .Any(entry => ReferenceEquals(entry.ToEntityEntry().Entity, child));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, probe.Writes);
        Assert.Equal(1, storedParent);
        Assert.Equal(3, storedNodes);
        Assert.Equal(1, storedNotes);
        Assert.True(remainsTracked);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>
    ///     Direct insertion and structural edits consistently identify the facade on every save boundary.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public async Task DirectHierarchyWritesIdentifyFacade(
        bool wrapped,
        bool automaticDetection,
        bool synchronous,
        bool insertion
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped, probe);
        var node = await context.Set<NavigationNode>().SingleAsync(value => value.Id == 2, CancellationToken.None);
        var originalLeft = node.Left;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (insertion)
        {
            await context.AddAsync(new NavigationNode
            {
                Id = 4,
                Scope = node.Scope,
                TreeId = s_treeId,
                ParentId = 1,
                Name = "Delta",
            }, CancellationToken.None);
        }
        else
        {
            node.Left += 100;
        }

        // Act
        var failure = await Record.ExceptionAsync(() => SaveNavigationAsync(context, synchronous));
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var storedLeft = await verification.Set<NavigationNode>().Where(value => value.Id == 2)
            .Select(value => value.Left).SingleAsync(CancellationToken.None);

        var storedNodes = await verification.Set<NavigationNode>().CountAsync(CancellationToken.None);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Contains("facade", rejection.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.Writes);
        Assert.Equal(originalLeft, storedLeft);
        Assert.Equal(3, storedNodes);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
