using System.Text.RegularExpressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Doka.EntityFrameworkCore.NestedSet.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerCompositionStrategyTests
{
    private const string Tag = "-- " + NestedSetCompositionTag.Text;

    private const string Hint = "OPTION (OPTIMIZE FOR UNKNOWN, MAXDOP 1)";

    /// <summary>Only EF's leading tag comment block identifies a composition command.</summary>
    /// <param name="commandText">The generated or adversarial command text.</param>
    /// <param name="expected">Whether the command carries the library tag.</param>
    [Theory]
    [InlineData(Tag + "\n\nSELECT 1", true)]
    [InlineData("-- caller\r\n" + Tag + "\r\n\r\nSELECT 1", true)]
    [InlineData(Tag + " \nSELECT 1", true)]
    [InlineData("SELECT 1 " + Tag, false)]
    [InlineData("SELECT N'" + Tag + "'", false)]
    [InlineData(Tag + " extra\nSELECT 1", false)]
    [InlineData("-- caller\n\nSELECT 1\n" + Tag, false)]
    [InlineData("", false)]
    public void CompositionTagMarksOnlyLeadingTagComments(string commandText, bool expected)
    {
        // Act
        var actual = NestedSetCompositionTag.Marks(commandText);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Every execution kind appends the statement hint once and keeps an optional terminator last.</summary>
    /// <param name="kind">The intercepted command execution kind.</param>
    /// <param name="commandText">The command text before interception.</param>
    /// <param name="expected">The exact command text after interception.</param>
    [Theory]
    [MemberData(nameof(PlanHintCases))]
    public async Task PlanInterceptorAppendsTheHintOnceToTaggedStatements(
        string kind,
        string commandText,
        string expected
    )
    {
        // Arrange
        var interceptor = new SqlServerCompositionPlanInterceptor();
        await using var command = new SqlCommand(commandText);

        // Act
        switch (kind)
        {
            case "Reader":
                _ = interceptor.ReaderExecuting(command, null!, default);
                break;
            case "ReaderAsync":
                _ = await interceptor.ReaderExecutingAsync(command, null!, default, CancellationToken.None);
                break;
            case "Scalar":
                _ = interceptor.ScalarExecuting(command, null!, default);
                break;
            case "ScalarAsync":
                _ = await interceptor.ScalarExecutingAsync(command, null!, default, CancellationToken.None);
                break;
            case "NonQuery":
                _ = interceptor.NonQueryExecuting(command, null!, default);
                break;
            default:
                _ = await interceptor.NonQueryExecutingAsync(command, null!, default, CancellationToken.None);
                break;
        }

        // Assert
        Assert.Equal(expected, command.CommandText);
    }

    /// <summary>Supplies each execution kind with tagged, terminated, repeated and untagged commands.</summary>
    public static TheoryData<string, string, string> PlanHintCases
    {
        get
        {
            var cases = new TheoryData<string, string, string>();
            var tagged = Tag + "\n\nSELECT 1";
            var hinted = tagged + Environment.NewLine + Hint;

            string[] kinds = ["Reader", "ReaderAsync", "Scalar", "ScalarAsync", "NonQuery", "NonQueryAsync"];

            foreach (var kind in kinds)
            {
                cases.Add(kind, tagged, hinted);
                cases.Add(kind, tagged + " ;\n", hinted + ";");
                cases.Add(kind, hinted, hinted);
                cases.Add(kind, "SELECT 1", "SELECT 1");
            }

            return cases;
        }
    }

    /// <summary>The SQL Server adapter registers its policy and tags composition, never ordinary reads.</summary>
    [Fact]
    public async Task SqlServerTagsCompositionAndRegistersItsPlanPolicy()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>("SqlServer"))
            .AddInterceptors(boundary);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        var tree = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty);

        // Act
        var composition = tree.SelectSubtrees(node => node.Id == 1, (anchor, node, distance) => node.Id)
            .ToQueryString();

        var nodes = tree.Nodes.Select(node => node.Id).ToQueryString();
        var interceptors = ((IInfrastructure<IServiceProvider>)context).Instance.GetServices<IInterceptor>();

        // Assert
        Assert.Matches("(?m)^" + Regex.Escape(Tag) + "\\r?$", composition);
        Assert.DoesNotContain(NestedSetCompositionTag.Text, nodes, StringComparison.Ordinal);
        Assert.Single(interceptors.OfType<SqlServerCompositionPlanInterceptor>());
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Adapters without a command policy keep their generated composition SQL unchanged.</summary>
    /// <param name="engine">The actual configured provider adapter.</param>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task OtherAdaptersKeepCompositionSqlUntagged(string engine)
    {
        // Arrange
        var native = engine == "Sqlite"
            ? new DbContextOptionsBuilder<CompositionContext>()
                .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()).Options
            : ModelCompatibilityDatabase.Options<CompositionContext>(engine);

        await using var services = NestedSetTestAdapters.BuildServices(native);
        await using var context = new CompositionContext(
            new DbContextOptionsBuilder<CompositionContext>(native).UseInternalServiceProvider(services).Options);

        // Act
        var sql = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty)
            .SelectSubtrees(node => node.Id == 1, (anchor, node, distance) => node.Id).ToQueryString();

        var interceptors = ((IInfrastructure<IServiceProvider>)context).Instance.GetServices<IInterceptor>();

        // Assert
        Assert.DoesNotContain(NestedSetCompositionTag.Text, sql, StringComparison.Ordinal);
        Assert.Empty(interceptors.OfType<SqlServerCompositionPlanInterceptor>());
    }
}
