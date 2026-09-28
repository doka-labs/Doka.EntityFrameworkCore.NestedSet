namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NestedSetMutationFacadeTests
{
    /// <summary>Detach cannot adopt an active destination tree or alter either tree's registry lifecycle.</summary>
    /// <returns>A task that completes after verifying active trees and registry revisions remain unchanged.</returns>
    [Fact]
    public async Task DetachRejectsActiveDestinationWithoutChangingEitherTree()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, s_firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 3 }, 2, CancellationToken.None);
        await hierarchy.InsertRootAsync(new TreeNode { NodeId = 10 }, s_secondTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new TreeNode { NodeId = 11 }, 10, CancellationToken.None);
        var structureBefore = await StructureAsync(context);
        var registryBefore = await ActiveTargetRegistriesAsync(context);

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.DetachAsTreeAsync(
            2,
            s_secondTree,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(structureBefore, await StructureAsync(context));
        Assert.Equal(registryBefore, await ActiveTargetRegistriesAsync(context));
        Assert.True(
            (await hierarchy
                .InTree(s_firstTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await hierarchy
                .InTree(s_secondTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Captures source and destination registry state to detect adoption or revision writes.</summary>
    private static Task<(Guid TreeId, long Revision, byte Lifecycle)[]> ActiveTargetRegistriesAsync(
        TreeContext context
    )
    {
        var registry = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(TreeNode))!).Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .OrderBy(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId))
            .Select(row => new ValueTuple<Guid, long, byte>(
                EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(CancellationToken.None);
    }
}
