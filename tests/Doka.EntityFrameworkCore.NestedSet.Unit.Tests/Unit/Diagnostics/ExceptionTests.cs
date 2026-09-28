namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Documents compatibility constructors for callers that create their own hierarchy rejection.</summary>
public sealed class ExceptionTests
{
    /// <summary>Compatibility constructors retain the original cause and use a bounded default error code.</summary>
    [Fact]
    public void UnclassifiedRejectionPreservesItsCause()
    {
        // Arrange
        var cause = new ArgumentException("Application validation failed.");

        // Act
        var error = new NestedSetException("The operation was rejected.", cause);

        // Assert
        Assert.Equal(NestedSetErrorCode.OperationRejected, error.Code);
        Assert.Same(cause, error.InnerException);
        Assert.IsType<InvalidOperationException>(error, exactMatch: false);
    }
}
