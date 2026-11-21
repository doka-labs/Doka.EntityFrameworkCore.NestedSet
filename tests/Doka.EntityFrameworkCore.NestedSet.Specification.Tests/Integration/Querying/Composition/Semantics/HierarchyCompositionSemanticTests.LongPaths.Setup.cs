namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Seeds only small independent scopes and observes the actual scalar query boundary.</summary>
    private async Task<CompositionContext> CreateLongPathContextAsync(
        Guid scope,
        int lower,
        CompositionNode[] nodes,
        CompositionCommandProbe probe,
        CancellationToken cancellationToken
    )
    {
        var context = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options),
            cancellationToken: cancellationToken,
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, probe.RecordMaterialization);

        try
        {
            await context.AddRangeAsync(nodes, cancellationToken);
            await context.AddRangeAsync(
                LongDays()
                    .Select(day => new CompositionInputOne
                    {
                        Scope = scope,
                        NodeId = lower,
                        Date = day,
                        Amount = 1,
                    }),
                cancellationToken);

            await context.SavePrecomputedHierarchyAsync(cancellationToken);
            context.ChangeTracker.Clear();
            probe.Reset();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }
}
