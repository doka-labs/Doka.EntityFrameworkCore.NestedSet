namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RetryBoundaryTests
{
    /// <summary>
    ///     Preserves the real provider options while explicitly owning intentional retry-service variants.
    /// </summary>
    private static SaveBoundaryContext Create(
        DbContext source,
        IServiceProvider? services,
        params IInterceptor[] interceptors
    )
    {
        var extensions = source
            .GetService<IDbContextOptions>()
            .Extensions
            .ToDictionary(extension => extension.GetType());

        var options = new DbContextOptionsBuilder<SaveBoundaryContext>(
                new DbContextOptions<SaveBoundaryContext>(extensions))
            .ConfigureTestWarnings()
            .AddInterceptors(interceptors);

        if (services is not null)
        {
            options.UseInternalServiceProvider(services);
        }

        return new SaveBoundaryContext(options.Options);
    }

    /// <summary>Seeds two siblings whose rename requires automatic reordering inside one tree.</summary>
    private static async Task SeedAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        await tree.InsertRootAsync(
            new OrderingNode
            {
                Id = 100,
                Name = "Root",
            },
            Guid.Empty,
            cancellationToken);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 1,
                Name = "Alpha",
            },
            100,
            cancellationToken);

        await tree.InsertChildAsync(
            new OrderingNode
            {
                Id = 2,
                Name = "Bravo",
            },
            100,
            cancellationToken);
    }
}
