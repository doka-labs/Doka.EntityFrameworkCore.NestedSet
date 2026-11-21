using System.Collections;
using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    private static readonly Lazy<GuardPlanAccess> s_guardPlans = new(static () => new GuardPlanAccess());

    /// <summary>Invokes private capture without reflection inside allocation measurement loops.</summary>
    internal static object CapturePlan(
        List<EntityEntry> candidates
    ) => s_guardPlans.Value.Capture(candidates);

    /// <summary>Invokes the actual complete-plan observer through a once-compiled test-only adapter.</summary>
    internal static bool PlanHasChanges(
        DbContext context,
        object plan
    ) => s_guardPlans.Value.HasChanges(context, plan);

    /// <summary>Reads the private dictionary's ordinary collection count without exposing its value type.</summary>
    internal static int PlanCount(
        object plan
    ) => ((IDictionary)plan).Count;

    /// <summary>Keeps private production types private while measuring their actual capture/validation paths.</summary>
    private sealed class GuardPlanAccess
    {
        /// <summary>Binds the two existing private methods once; no test-only production API is introduced.</summary>
        internal GuardPlanAccess()
        {
            var methods = typeof(NestedSetSaveChanges).GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
            var capture = methods.Single(method => method.Name == "CaptureGuardPlan");
            var observer = methods.Single(method => method.Name == "HasHierarchyChanges"
                && method.GetParameters()
                    .Length
                == 4);

            var candidates = Expression.Parameter(typeof(List<EntityEntry>), "candidates");
            Capture = Expression
                .Lambda<Func<List<EntityEntry>, object>>(
                    Expression.Convert(Expression.Call(capture, candidates), typeof(object)),
                    candidates)
                .Compile();

            var context = Expression.Parameter(typeof(DbContext), "context");
            var plan = Expression.Parameter(typeof(object), "plan");
            var parameters = observer.GetParameters();
            HasChanges = Expression
                .Lambda<Func<DbContext, object, bool>>(
                    Expression.Call(
                        observer,
                        context,
                        Expression.Convert(plan, parameters[1].ParameterType),
                        Expression.Constant(null, parameters[2].ParameterType),
                        Expression.Constant(false)),
                    context,
                    plan)
                .Compile();
        }

        /// <summary>Gets the cached capture delegate used by functional and resource regressions.</summary>
        internal Func<List<EntityEntry>, object> Capture { get; }

        /// <summary>Gets the cached observer delegate used by functional and resource regressions.</summary>
        internal Func<DbContext, object, bool> HasChanges { get; }
    }
}
