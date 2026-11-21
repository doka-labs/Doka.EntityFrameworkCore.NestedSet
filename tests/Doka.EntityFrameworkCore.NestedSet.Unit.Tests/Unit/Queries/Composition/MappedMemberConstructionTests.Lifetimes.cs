using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class MappedMemberConstructionTests
{
    /// <summary>Builds the mapped read on detached runtime metadata before dropping its construction frames.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference[] PopulateWeakMembers(
        bool retainModel,
        out IModel? retained
    )
    {
        var options = new DbContextOptionsBuilder<ModelContext>().UseSqlite("Data Source=:memory:");
        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        using var provider = services.BuildServiceProvider();
        using var context = new ModelContext(
            options
                .UseInternalServiceProvider(provider)
                .Options);

        var builder = new ModelBuilder(
            context
                .GetService<IConventionSetBuilder>()
                .CreateConventionSet());

        builder.SharedTypeEntity<MemberNode>(
            "Mapped",
            mapping =>
            {
                mapping.HasNoKey();
                mapping.Property(node => node.Value);
            });

        // WHY: Pending DI resolver compilation can retain the provider and its cached context model.
        // Initialize this independent model through EF's public services without entering ModelSource's
        // cache, so that unrelated background work cannot become a root in this ownership counterexample.
        var model = Assert.IsType<RuntimeModel>(
            context
                .GetService<IModelRuntimeInitializer>()
                .Initialize(builder.FinalizeModel(), designTime: false));

        var entity = ((IModel)model).FindEntityType("Mapped")!;
        var property = entity.FindProperty(nameof(MemberNode.Value))!;

        var alias = Expression.Parameter(typeof(MemberNode), "node");
        _ = NestedSetExpressions.QueryProperty(alias, property);
        retained = retainModel
            ? model
            : null;

        return
        [
            new WeakReference(model), new WeakReference(entity), new WeakReference(property),
            new WeakReference(alias),
        ];
    }

    /// <summary>
    /// Separates garbage collection from weak-target observation to avoid extending JIT local lifetimes.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void CollectGarbage()
    {
        // WHY: Complete repeated collect/finalizer passes before observing collectible model and member witnesses.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var attempt = 0; attempt < 8; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

}
