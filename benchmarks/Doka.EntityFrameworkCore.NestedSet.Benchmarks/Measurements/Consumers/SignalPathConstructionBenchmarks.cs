using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures the complete direct, two-edge and long-path proof on the real shadow-property model.</summary>
public class SignalPathConstructionBenchmarks : IAsyncDisposable
{
    private ConsumerMetadataServices<SignalLabContext>? _services;
    private SignalLabContext? _context;
    private Func<Expression, Expression, ParameterExpression, LambdaExpression?, ConditionalExpression>? _build;
    private readonly ParameterExpression _upper = Expression.Parameter(typeof(SignalNode), "upper");
    private readonly ParameterExpression _lower = Expression.Parameter(typeof(SignalNode), "lower");
    private readonly ParameterExpression _row = Expression.Parameter(typeof(object), "row");

    /// <summary>Gets or sets the original dialect variants of path construction.</summary>
    [Params("Sqlite", "MariaDb", "MySql")]
    public string Engine { get; set; } = "Sqlite";

    /// <summary>Binds the actual private proof once, outside measured invocation work.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        _services = ConsumerMetadataServices<SignalLabContext>.Create(Engine);
        _context = new SignalLabContext(_services.Options);
        var query = new NestedSetQuery<SignalNode>(_context, _context.Model.FindEntityType(typeof(SignalNode))!)
            .ForScope(Guid.Parse("17d85da0-221c-4dd0-a51e-000000000001"));

        // WHY: This existing framework seam measures the complete actual proof, not a benchmark copy of its logic.
        _build = typeof(NestedSetQuery<SignalNode>)
            .GetMethod("PathEdgeEvidence", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<
                Func<Expression, Expression, ParameterExpression, LambdaExpression?, ConditionalExpression>>(query);
    }

    /// <summary>Builds every path branch for the already bound independent endpoints.</summary>
    /// <returns>The actual deferred proof expression.</returns>
    [Benchmark]
    public object Build() => (_build ?? throw new InvalidOperationException("Bind the path builder first."))(
        _upper, _lower, _row, null);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _build = null;
        var context = _context;
        var services = _services;
        _context = null;
        _services = null;
        if (services is not null)
        {
            await services.DisposeContextAsync(context);
        }
        else if (context is not null)
        {
            await context.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the context before its owned provider services.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();
}
