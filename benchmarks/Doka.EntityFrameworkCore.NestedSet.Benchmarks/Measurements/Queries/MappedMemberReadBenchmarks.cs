using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes finalized mapped-member construction beside ordinary expression member construction.</summary>
public class MappedMemberReadBenchmarks : IDisposable
{
    private QueryMeasurementModel _model = null!;
    private IProperty _property = null!;
    private ParameterExpression _alias = null!;

    /// <summary>Creates actual detached runtime property metadata outside measured invocations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _model = new QueryMeasurementModel();
        var runtime = (RuntimeModel)_model.Context.Model;
        var entity = runtime.AddEntityType("MappedRead", typeof(MappedReadNode), sharedClrType: true);
        _property = entity.AddProperty(nameof(MappedReadNode.Value), typeof(int),
            propertyInfo: typeof(MappedReadNode).GetProperty(nameof(MappedReadNode.Value)));
        _alias = Expression.Parameter(typeof(MappedReadNode), "node");
        Validate();
    }

    /// <summary>Constructs a standard member expression for the same alias and property.</summary>
    /// <returns>The ordinary deferred member read.</returns>
    [Benchmark(Baseline = true)]
    public Expression DirectMember() => Expression.MakeMemberAccess(_alias, _property.PropertyInfo!);

    /// <summary>Constructs the actual mapped query read for the same runtime property.</summary>
    /// <returns>The mapped deferred member read.</returns>
    [Benchmark]
    public Expression MappedMember() => NestedSetExpressions.QueryProperty(_alias, _property);

    /// <summary>Rejects alias or member changes independently of allocation observations.</summary>
    public void Validate()
    {
        if (MappedMember() is not MemberExpression member
            || !ReferenceEquals(member.Expression, _alias)
            || member.Member != _property.PropertyInfo)
        {
            throw new InvalidOperationException("Mapped-member construction changed its alias or property.");
        }
    }

    /// <summary>Validates the operation and releases its independently owned provider.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            Validate();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>Releases owned setup state independently of semantic cleanup validation.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed setup resources when disposal was explicitly requested.</summary>
    /// <param name="disposing">Whether managed state can be released.</param>
    protected virtual void Dispose(
        bool disposing
    )
    {
        if (disposing)
        {
            _model?.Dispose();
        }
    }

    /// <summary>Rejects any accidental application getter evaluation during expression construction.</summary>
    private sealed class MappedReadNode
    {
        private readonly InvalidOperationException _getterFailure = new("Mapped getters must remain deferred.");

        /// <summary>Gets a marker that must remain deferred.</summary>
        public int Value => throw _getterFailure;
    }
}
