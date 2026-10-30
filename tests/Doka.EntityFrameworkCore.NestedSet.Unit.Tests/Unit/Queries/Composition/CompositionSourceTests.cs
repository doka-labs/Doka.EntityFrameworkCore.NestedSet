using System.Collections;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Rejects non-EF and foreign-context sources before enumeration or I/O.</summary>
public sealed class CompositionSourceTests
{
    /// <summary>Plausible entity keys do not make an in-memory array a hierarchy query source.</summary>
    /// <param name="operation">The public source filter being checked.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void ArrayQueryCannotSupplyHierarchyAliases(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var node = new CompositionGuardNode { Id = 1 };
        var source = new[] { new CompositionGuardRow { Upper = node, Lower = node } }.AsQueryable();

        // Act
        var error = Record.Exception(() => CompositionEndpointTests.Filter(hierarchy, source, operation));

        // Assert
        Assert.IsAssignableFrom<EnumerableQuery<CompositionGuardRow>>(source);
        Assert.Equal("source", Assert.IsType<ArgumentException>(error).ParamName);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>EnumerableQuery sources are rejected without visiting an input row.</summary>
    /// <param name="operation">The public source filter being checked.</param>
    /// <param name="filtered">Whether LINQ-to-Objects operators precede AsQueryable.</param>
    [Theory]
    [InlineData("path", false)]
    [InlineData("path", true)]
    [InlineData("any", false)]
    [InlineData("any", true)]
    [InlineData("nearest", false)]
    [InlineData("nearest", true)]
    public void EnumerableQueryIsRejectedWithoutEnumeration(
        string operation,
        bool filtered
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var input = new ForbiddenEnumeration();
        var source = filtered ? input.AsEnumerable().Where(row => row.Distance > 0).AsQueryable() : input.AsQueryable();

        // Act
        var error = Record.Exception(() => CompositionEndpointTests.Filter(hierarchy, source, operation));

        // Assert
        Assert.IsAssignableFrom<EnumerableQuery<CompositionGuardRow>>(source);
        Assert.Equal("source", Assert.IsType<ArgumentException>(error).ParamName);
        Assert.Equal(0, input.EnumerationStarts);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>The same provider implementation does not make two DbContext instances interchangeable.</summary>
    /// <param name="operation">The public source filter being checked.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void ForeignContextSourceIsRejectedBeforeIo(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        using var foreign = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var source = foreign.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping)
            .Select(node => new CompositionGuardRow { Upper = node, Lower = node });

        // Act
        var error = Record.Exception(() => CompositionEndpointTests.Filter(hierarchy, source, operation));

        // Assert
        Assert.Equal("source", Assert.IsType<ArgumentException>(error).ParamName);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Provides an actual LINQ-to-Objects source whose accidental enumeration fails immediately.</summary>
    private sealed class ForbiddenEnumeration : IEnumerable<CompositionGuardRow>
    {
        /// <summary>Gets how often a query builder attempted to read the source.</summary>
        internal int EnumerationStarts { get; private set; }

        /// <inheritdoc />
        public IEnumerator<CompositionGuardRow> GetEnumerator()
        {
            EnumerationStarts++;

            throw new InvalidOperationException("Query composition must not enumerate application input.");
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
