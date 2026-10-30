namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Seeds neutral hierarchy payloads shared by composition and semantic contracts.</summary>
internal static class CompositionTestData
{
    /// <summary>Creates a neutral six-node tree without coupling its payload to a consumer domain.</summary>
    internal static async Task<CompositionContext> CreateContextAsync(
        ModelCompatibilityDatabase fixture,
        string engine,
        Guid scope,
        CompositionCommandProbe probe,
        int? hidden = null,
        int? closed = null,
        EnterpriseProbe? planProbe = null,
        int? chainNodes = null,
        ChainOwnerMode ownerMode = ChainOwnerMode.RootValue
    )
    {
        var context = await fixture.CreateContextAsync<CompositionContext>(
            engine,
            static options => new CompositionContext(options),
            planProbe is null
                ? [probe, TestMaterializationObserver.Instance]
                : [probe, planProbe, TestMaterializationObserver.Instance]);

        TestMaterializationObserver.Instance.Register(context, probe.RecordMaterialization);

        try
        {
            var treeId = Guid.NewGuid();
            var nodes = chainNodes is { } count
                ? CreateChain(scope, treeId, count, ownerMode)
                :
                [
                    Node(1, null, 1, 12, 0, 0),
                    Node(2, 1, 2, 7, 1, 0),
                    Node(3, 2, 3, 4, 2, 0),
                    Node(4, 2, 5, 6, 2, 1),
                    Node(5, 1, 8, 11, 1, 1),
                    Node(6, 5, 9, 10, 2, 0),
                ];

            // WHY: Every case has its own scope, so stable oracle keys can coexist without reset races.
            await context.AddRangeAsync(nodes, CancellationToken.None);
            await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return context;

            CompositionNode Node(
                int id,
                int? parent,
                long left,
                long right,
                int depth,
                long position
            ) => new()
            {
                Scope = scope,
                TreeId = treeId,
                Id = id,
                ParentId = parent,
                Left = left,
                Right = right,
                Depth = depth,
                Position = position,
                Visible = id != hidden,
                EdgeOpen = id != closed,
                Candidate = id is 1 or 2,
                Matches = id == 1,
                ToDate = id == 2 ? new DateOnly(2044, 10, 31) : null,
            };
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Seeds dense owner candidates without recursively traversing the deep test hierarchy.</summary>
    private static CompositionNode[] CreateChain(
        Guid scope,
        Guid treeId,
        int count,
        ChainOwnerMode ownerMode
    )
    {
        var nodes = new CompositionNode[count];

        for (var index = 0; index < nodes.Length; index++)
        {
            nodes[index] = new CompositionNode
            {
                Scope = scope,
                TreeId = treeId,
                Id = index + 1,
                ParentId = index == 0 ? null : index,
                Left = index + 1,
                Right = (2L * count) - index,
                Depth = index,
                Candidate = ownerMode != ChainOwnerMode.RootOnly || index == 0,
                Matches = ownerMode == ChainOwnerMode.AllValues || index == 0,
            };
        }

        return nodes;
    }

    /// <summary>Separates dense rejection, dense acceptance, and one long selected-owner path.</summary>
    internal enum ChainOwnerMode
    {
        /// <summary>Every node is eligible, but only the outermost owner passes the value predicate.</summary>
        RootValue,

        /// <summary>Every node is eligible and passes the value predicate.</summary>
        AllValues,

        /// <summary>Only the root is eligible, so its complete path to the selected leaf is checked.</summary>
        RootOnly,
    }

}
