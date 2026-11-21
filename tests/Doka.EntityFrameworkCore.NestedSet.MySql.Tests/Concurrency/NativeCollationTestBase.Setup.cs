using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class NativeCollationTestBase
{
    /// <summary>Creates the unknown-facet model in a schema whose native default no other suite can change.</summary>
    /// <typeparam name="TContext">The exact normal or converted-key compatibility model.</typeparam>
    /// <param name="database">The independently owned schema on the existing assembly server.</param>
    /// <param name="create">Constructs the exact mapped context from its provider options.</param>
    /// <param name="probe">The observer for the guard operation.</param>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>The ready context, whose lifetime transfers to the caller.</returns>
    private async Task<TContext> CreateUnknownDatabaseContextAsync<TContext>(
        TestDatabase database,
        Func<DbContextOptions<TContext>, TContext> create,
        NativeGuardProbe probe,
        CancellationToken cancellationToken
    )
        where TContext : BroadScopeContext
    {
        await using var source = database.CreateContext();
        var options = new DbContextOptionsBuilder<TContext>(
                ModelCompatibilityDatabase.Options<TContext>(Engine, source.Database.GetConnectionString()!))
            .AddInterceptors(probe)
            .Options;

        var context = create(options);

        try
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Selects distinct cached models for inherited collations and verified/scalar key transport.</summary>
    /// <param name="engine">The immutable engine selected by the concrete suite fixture.</param>
    /// <param name="convertedKeys">Whether to exercise the converted NodeKey transport.</param>
    /// <param name="noPad">Whether to use MySQL's table-level NO PAD comparison.</param>
    /// <param name="probe">The native command observer for this independently arranged scenario.</param>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>The existing compatibility model with its exact configured physical collation.</returns>
    private protected async Task<BroadScopeContext> CreateInheritedScopeContextAsync(
        string engine,
        bool convertedKeys,
        bool noPad,
        NativeGuardProbe probe,
        CancellationToken cancellationToken
    )
    {
        if (noPad)
        {
            return convertedKeys
                ? await _fixture.CreateContextAsync<ConvertedInheritedNoPadScopeContext>(
                    engine,
                    static options => new ConvertedInheritedNoPadScopeContext(options),
                    cancellationToken: cancellationToken,
                    probe)
                : await _fixture.CreateContextAsync<InheritedNoPadScopeContext>(
                    engine,
                    static options => new InheritedNoPadScopeContext(options),
                    cancellationToken: cancellationToken,
                    probe);
        }

        return convertedKeys
            ? await _fixture.CreateContextAsync<ConvertedInheritedBinaryScopeContext>(
                engine,
                static options => new ConvertedInheritedBinaryScopeContext(options),
                cancellationToken: cancellationToken,
                probe)
            : await _fixture.CreateContextAsync<InheritedBinaryScopeContext>(
                engine,
                static options => new InheritedBinaryScopeContext(options),
                cancellationToken: cancellationToken,
                probe);
    }
}
