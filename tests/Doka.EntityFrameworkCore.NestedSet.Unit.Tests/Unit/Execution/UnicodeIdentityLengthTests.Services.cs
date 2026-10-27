namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnicodeIdentityLengthTests
{
    /// <summary>Isolates real Unicode mapping services and disposes their owned graph with the context.</summary>
    /// <param name="asynchronous">Whether to exercise asynchronous rather than explicitly synchronous disposal.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnicodeMappingsOwnServicesAndReleaseThemWithContext(bool asynchronous)
    {
        // Arrange
        await using var first = new UnicodeContext("Sqlite");
        await using var second = new UnicodeContext("Sqlite");
        var firstServices = first.GetService<IModelSource>();
        var secondServices = second.GetService<IModelSource>();
        var firstOwner = first.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!
            .InternalServiceProvider;

        // Act
        if (asynchronous)
        {
            await first.DisposeAsync();
        }
        else
        {
            // WHY: The synchronous context contract must release its external provider too.
            first.Dispose();
        }

        var disposed = Record.Exception(() => firstOwner?.GetService(typeof(IModelSource)));

        // Assert
        Assert.NotNull(firstOwner);
        Assert.NotSame(firstServices, secondServices);
        Assert.IsType<ObjectDisposedException>(disposed);
        Assert.NotNull(second.Model.FindEntityType(UnicodeContext.Raw));
        Assert.NotNull(second.Model.FindEntityType(UnicodeContext.Converted));
        Assert.NotNull(second.Model.FindEntityType(UnicodeContext.Binary));
        Assert.Equal(ConnectionState.Closed, second.Database.GetDbConnection().State);
        Assert.Empty(second.ChangeTracker.Entries());
    }
}
