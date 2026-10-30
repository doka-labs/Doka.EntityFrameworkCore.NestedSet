using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Only SQL Server completes an independent fact selection before an inclusive ancestor range.</summary>
    /// <param name="engine">The configured provider adapter.</param>
    /// <param name="shape">An independent fact, hierarchy-backed or plain selection.</param>
    /// <param name="completed">Whether the selection must be completed behind a node-key boundary.</param>
    [Theory]
    [InlineData("SqlServer", "facts", true)]
    [InlineData("SqlServer", "hierarchy", false)]
    [InlineData("SqlServer", "plain", false)]
    [InlineData("PostgreSql", "facts", false)]
    [InlineData("Sqlite", "facts", false)]
    public async Task InclusiveAncestorsCompleteOnlyIndependentFactSelections(
        string engine,
        string shape,
        bool completed
    )
    {
        // Arrange
        var connection = engine switch
        {
            "Sqlite" => "Data Source=:memory:",
            "PostgreSql" => "Host=localhost;Database=completed_facts;Username=unused",
            _ => "Server=127.0.0.1;Database=completed_facts;User ID=unused;Pooling=false",
        };

        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>(engine, connection);
        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options).UseInternalServiceProvider(provider).Options);

        var facts = context.Set<SelectionFact>();
        var nodes = context.Set<CompositionNode>();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty);
        var query = shape switch
        {
            "facts" => hierarchy.SelectAncestorsOrSelf(node => facts.Any(fact => fact.NodeId == node.Id),
                (node, owner, distance) => owner.Id),
            "hierarchy" => hierarchy.SelectAncestorsOrSelf(node => nodes.Any(other => other.Id == node.Id + 1),
                (node, owner, distance) => owner.Id),
            _ => hierarchy.SelectAncestorsOrSelf(node => node.Id > 0, (node, owner, distance) => owner.Id),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(completed, Regex.IsMatch(sql, @"\[Id\] DESC\s+OFFSET 0 ROWS"));
        Assert.Equal(completed, Regex.IsMatch(sql, @"FROM \(\s*SELECT "));
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
