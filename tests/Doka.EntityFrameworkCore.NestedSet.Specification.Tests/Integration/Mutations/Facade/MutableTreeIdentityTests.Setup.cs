namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class MutableTreeIdentityTests
{

    /// <summary>Creates three independently addressable siblings beneath one binary-key root.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private static async Task SeedBinarySiblingsAsync(
        ScopedNestedSet<BinaryNode, int> hierarchy,
        CancellationToken cancellationToken
    )
    {
        await hierarchy.InsertRootAsync(new BinaryNode { Id = [1, 1] }, Guid.Empty, cancellationToken);
        await hierarchy.InsertChildAsync<byte[]>(new BinaryNode { Id = [2, 2] }, [1, 1], cancellationToken);
        await hierarchy.InsertChildAsync<byte[]>(new BinaryNode { Id = [3, 3] }, [1, 1], cancellationToken);
        await hierarchy.InsertChildAsync<byte[]>(new BinaryNode { Id = [4, 4] }, [1, 1], cancellationToken);
    }
}
