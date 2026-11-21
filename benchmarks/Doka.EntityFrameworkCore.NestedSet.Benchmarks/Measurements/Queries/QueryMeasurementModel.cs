using Microsoft.Extensions.DependencyInjection;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns independent real-provider metadata for disconnected query-construction observations.</summary>
internal sealed class QueryMeasurementModel : IDisposable
{
    internal const string FirstMapping = "MeasurementFirst";
    internal const string SecondMapping = "MeasurementSecond";
    internal const string ScopedMapping = "MeasurementScoped";

    private readonly ServiceProvider _services;

    /// <summary>Builds the real SQLite provider without publishing benchmark variants into EF's global cache.</summary>
    internal QueryMeasurementModel(
        Action<DbContextOptionsBuilder>? configure = null
    )
    {
        var options = new DbContextOptionsBuilder<MeasurementContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());

        configure?.Invoke(options);
        var services = new ServiceCollection();

        // WHY: Model variants are setup inputs; independently owned provider services preserve their lifetime.
        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        _services = services.BuildServiceProvider();
        Context = new MeasurementContext(options.UseInternalServiceProvider(_services).Options);
        _ = Context.Model;
    }

    /// <summary>Gets the unopened context whose finalized metadata belongs to this workload.</summary>
    internal DbContext Context { get; }

    /// <summary>Gets a deferred named hierarchy source without enumerating or translating it.</summary>
    internal IQueryable<BenchmarkNode> Nodes(
        string mapping
    ) => Context.Set<BenchmarkNode>(mapping);

    /// <summary>Releases scoped context state before the provider that supplied it.</summary>
    public void Dispose()
    {
        try
        {
            Context.Dispose();
        }
        finally
        {
            _services.Dispose();
        }
    }

    /// <summary>Maps identical CLR nodes independently for lexical provenance and fused binding observations.</summary>
    private sealed class MeasurementContext : DbContext
    {
        internal MeasurementContext(
            DbContextOptions<MeasurementContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            foreach (var mapping in new[] { FirstMapping, SecondMapping, ScopedMapping })
            {
                modelBuilder.SharedTypeEntity<BenchmarkNode>(mapping, entity =>
                {
                    entity.ToTable(mapping);
                    entity.HasKey(node => node.Id);
                    entity.Property(node => node.Id).ValueGeneratedNever();
                    entity.HasNestedSet(hierarchy =>
                    {
                        hierarchy.HasNodeKey(node => node.Id)
                            .HasTreeId(node => node.TreeId)
                            .HasParent(node => node.ParentId)
                            .HasBounds(node => node.Left, node => node.Right)
                            .HasDepth(node => node.Depth)
                            .HasPosition(node => node.Position);

                        if (mapping == ScopedMapping)
                        {
                            hierarchy.HasScope(node => node.Scope);
                        }
                    });
                });
            }
        }
    }
}
