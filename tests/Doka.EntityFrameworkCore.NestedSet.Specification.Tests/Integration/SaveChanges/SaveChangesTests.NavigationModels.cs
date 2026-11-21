namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Chooses the exact synchronous or asynchronous application boundary requested by one test case.
    /// </summary>
    private static Task<int> SaveNavigationAsync(
        DbContext context,
        bool synchronous,
        CancellationToken cancellationToken
    )
    {
        if (synchronous)
        {
            // WHY: The synchronous cases must exercise SaveChanges itself, including its native callback sequence.
            // ReSharper disable once MethodHasAsyncOverload
            return Task.FromResult(context.SaveChanges());
        }

        return context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Requires the exact library or EF immutable-key rejection produced by this mapped scope.</summary>
    private static void AssertScopeChangeRejected(
        Exception? failure,
        DbContext context,
        bool wrapped,
        bool synchronous,
        bool automaticDetection
    )
    {
        if (wrapped
            && !synchronous
            && automaticDetection)
        {
            // WHY: Scope also identifies self-FK principals, so coordinated detection reaches EF's immutable key.
            var rejection = Assert.IsType<InvalidOperationException>(failure);
            var metadata = context.Model.FindEntityType(typeof(NavigationNode))!;
            Assert.Equal(
                CoreStrings.KeyReadOnly(nameof(NavigationNode.Scope), metadata.DisplayName()),
                rejection.Message);
        }
        else
        {
            Assert.Equal(
                NestedSetErrorCode.InvalidContext,
                Assert.IsType<NestedSetException>(failure)
                    .Code);
        }
    }
}
