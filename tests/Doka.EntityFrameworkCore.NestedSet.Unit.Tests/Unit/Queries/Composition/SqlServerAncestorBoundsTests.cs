using System.Text.RegularExpressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects independent ancestor coordinates and the selected SQL Server adapter boundary.</summary>
public sealed partial class SqlServerAncestorBoundsTests
{
    /// <summary>Only structural bounds cross the independent relation, leaving mapped keys for late hydration.</summary>
    /// <param name="treeBound">Whether the query targets one exact tree instead of the scope's forest.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictAncestorsKeepANonKeyDistinctBoundary(bool treeBound)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>("SqlServer"))
            .AddInterceptors(boundary);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty);
        var query = treeBound
            ? hierarchy.InTree(Guid.Empty).SelectAncestors(node => node.Id >= 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance })
            : hierarchy.SelectAncestors(node => node.Id >= 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        // Act
        var sql = query.ToQueryString();
        var distinct = Regex.Matches(sql, @"SELECT DISTINCT\s+(?<columns>.*?)\s+FROM", RegexOptions.Singleline);

        // Assert
        Assert.True(NestedSetProviderCapabilities.Resolve(context).UsesIndependentAncestorOwners);
        var columns = Assert.Single(distinct).Groups["columns"].Value;
        Assert.Contains("[TreeId]", columns, StringComparison.Ordinal);
        Assert.Contains("[Left]", columns, StringComparison.Ordinal);
        Assert.Contains("[Right]", columns, StringComparison.Ordinal);
        Assert.DoesNotContain("[Id]", columns, StringComparison.Ordinal);
        Assert.DoesNotContain("[Depth]", columns, StringComparison.Ordinal);
        Assert.DoesNotContain("[EdgeOpen]", columns, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A coordinate strategy is not an optimized reader for the optional generated ancestor index.</summary>
    [Fact]
    public async Task IndependentCoordinatesStillReportSchemaOnlyAncestorIndex()
    {
        // Arrange
        var events = new List<EventData>();
        var options = new DbContextOptionsBuilder<IndexedBoundsContext>(
                ModelCompatibilityDatabase.Options<IndexedBoundsContext>("SqlServer"))
            .LogTo((eventId, _) => eventId.Id == 70_004, events.Add);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new IndexedBoundsContext(options.UseInternalServiceProvider(services).Options);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty);

        // Act
        _ = hierarchy.SelectAncestors(node => node.Id == 3, (source, owner, distance) => owner.Id);

        // Assert
        Assert.True(NestedSetProviderCapabilities.Resolve(context).UsesIndependentAncestorOwners);
        Assert.Equal(70_004, Assert.Single(events).EventId.Id);
    }

    /// <summary>Requests the additive index without declaring that the adapter can execute its optimized reader.</summary>
    private sealed class IndexedBoundsContext : DbContext
    {
        /// <summary>Creates the metadata-only context in its owned provider graph.</summary>
        internal IndexedBoundsContext(DbContextOptions<IndexedBoundsContext> options) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var node = modelBuilder.Entity<CompositionNode>();
            node.HasKey(entity => new { entity.Scope, entity.Id });
            node.HasNestedSet(hierarchy => hierarchy
                .HasNodeKey(entity => entity.Id)
                .HasScope(entity => entity.Scope)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position)
                .HasAncestorIndex());
        }
    }
}
