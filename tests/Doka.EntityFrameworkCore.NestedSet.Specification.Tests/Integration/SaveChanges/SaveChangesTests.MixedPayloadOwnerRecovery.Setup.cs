namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Seeds B's original relationship independently of the callback whose rollback is tested.</summary>
    private static async Task SeedMixedPayloadOwnerMemberAsync(
        TestDatabase database,
        bool leavesTarget,
        CancellationToken cancellationToken
    )
    {
        await SeedPayloadOwnersAsync(database, assigned: true, cancellationToken: cancellationToken);
        await using var setup = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: cancellationToken);

        var owner = leavesTarget
            ? 2
            : 1;

        await setup
            .Set<NavigationNode>()
            .Where(node => node.Id == 3)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.PayloadOwnerId, owner),
                cancellationToken);
    }

    /// <summary>Retains both existing entries before a callback mixes raw assignment with public EF fixup.</summary>
    private static async Task<MixedPayloadOwnerGraph> PrepareMixedPayloadOwnerGraphAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        var a = await PreparePayloadOwnerRecoveryAsync(context, cancellationToken: cancellationToken);
        var b = await context
            .Set<NavigationNode>()
            .SingleAsync(node => node.Id == 3, cancellationToken);

        var entry = context.Entry(b);

        return new MixedPayloadOwnerGraph(a, entry, entry.Property(node => node.PayloadOwnerId));
    }
}
