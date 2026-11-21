using System.Collections;
using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    private static readonly Lazy<GuardPlanAccess> s_guardPlans = new(static () => new GuardPlanAccess());

    /// <summary>A Parent-only candidate remains authorized after suppression makes it Unchanged.</summary>
    [Fact]
    public async Task PlanHasChanges_SuppressedParentOnlyCandidate_RetainsExactAuthorization()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode(2);
        node.ParentId = 1;
        var entry = context.Attach(node);
        node.ParentId = 3;
        entry.Property(value => value.ParentId)
            .IsModified = true;

        entry.Property(value => value.ParentId)
            .IsModified = false;

        var plan = CapturePlan([entry]);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.False(changed);
        Assert.Equal(1, PlanCount(plan));
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(1, node.ParentId);
        Assert.False(
            entry.Property(value => value.ParentId)
                .IsModified);

        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Every raw role remains guarded even when a different property authorized the candidate.</summary>
    /// <param name="propertyName">The otherwise unmarked structural or ordering property changed by a callback.</param>
    [Theory]
    [InlineData(nameof(ProbeNode.Id))]
    [InlineData(nameof(ProbeNode.Scope))]
    [InlineData(nameof(ProbeNode.TreeId))]
    [InlineData(nameof(ProbeNode.ParentId))]
    [InlineData(nameof(ProbeNode.Left))]
    [InlineData(nameof(ProbeNode.Right))]
    [InlineData(nameof(ProbeNode.Depth))]
    [InlineData(nameof(ProbeNode.Position))]
    [InlineData(nameof(ProbeNode.Name))]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsUnmarkedRawRoleChanges(
        string propertyName
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = CreateNode();
        var entry = context.Attach(node);
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        ChangeRole(node, propertyName);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(
            propertyName == nameof(ProbeNode.Name),
            entry.Property(propertyName)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Clearing an authorized flag cannot be hidden by equal current and original scalar values.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsClearedOrderingFlag()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        entry.Property(value => value.Payload)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.Property(value => value.Name)
            .IsModified = false;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(
            entry.Property(value => value.Name)
                .OriginalValue,
            entry.Entity.Name);

        Assert.False(
            entry.Property(value => value.Name)
                .IsModified);
    }

    /// <summary>A newly introduced managed flag is rejected even when its scalar value has not changed.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedCandidate_RejectsIntroducedStructuralFlag()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.Property(value => value.Depth)
            .IsModified = true;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(0, entry.Entity.Depth);
        Assert.Equal(
            0,
            entry.Property(value => value.Depth)
                .OriginalValue);
    }

    /// <summary>Ordinary payload can change a candidate's state without changing its hierarchy plan.</summary>
    [Fact]
    public async Task PlanHasChanges_AuthorizedUnchangedCandidate_AllowsOrdinaryPayloadFlags()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        var plan = CapturePlan([entry]);
        entry.Entity.Payload = "Callback payload";
        entry.Property(value => value.Payload)
            .IsModified = true;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.False(changed);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.True(
            entry.Property(value => value.Payload)
                .IsModified);

        Assert.False(
            entry.Property(value => value.Name)
                .IsModified);
    }

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
