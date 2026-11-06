using System.Collections;
using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    private static readonly Lazy<GuardPlanAccess> s_guardPlans = new(static () => new GuardPlanAccess());

    /// <summary>A Parent-only candidate remains authorized after suppression makes it Unchanged.</summary>
    [Fact]
    public async Task SuppressedParentOnlyCandidateRetainsExactAuthorization()
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
    public async Task AuthorizedCandidateRejectsUnmarkedRawRoleChanges(
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
    public async Task AuthorizedCandidateRejectsClearedOrderingFlag()
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
    public async Task AuthorizedCandidateRejectsIntroducedStructuralFlag()
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

    /// <summary>Original-value tampering is visible independently of current values and modification flags.</summary>
    [Fact]
    public async Task AuthorizedCandidateRejectsChangedOrderingOriginal()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.Property(value => value.Name)
            .OriginalValue = "Different original";

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal("Alpha", entry.Entity.Name);
        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);
    }

    /// <summary>Ordinary payload can change a candidate's state without changing its hierarchy plan.</summary>
    [Fact]
    public async Task AuthorizedUnchangedCandidateAllowsOrdinaryPayloadFlags()
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

    /// <summary>Detached candidates cannot be replaced with an equal-key object to reuse their authorization.</summary>
    [Fact]
    public async Task AuthorizedCandidateRequiresExactTrackedEntry()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.State = EntityState.Detached;
        var replacement = CreateNode();
        context.Attach(replacement);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Detached, entry.State);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(replacement)
                .State);

        Assert.Equal(1, PlanCount(plan));
    }

    /// <summary>A suppressed Parent-only candidate cannot be deleted while all captured flags remain false.</summary>
    [Fact]
    public async Task AuthorizedUnchangedCandidateRejectsDeletion()
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
        entry.State = EntityState.Deleted;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(EntityState.Deleted, entry.State);
        Assert.Equal(1, node.ParentId);
        Assert.Equal(1, PlanCount(plan));
        Assert.False(
            entry.Property(value => value.ParentId)
                .IsModified);

        Assert.False(
            entry.Property(value => value.Name)
                .IsModified);

        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Unmarked secondary ordering criteria remain guarded beside an explicitly modified first one.</summary>
    [Fact]
    public async Task AuthorizedCandidateRejectsUnmarkedSecondaryOrderChange()
    {
        // Arrange
        await using var context = new SecondaryOrderingPlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        entry.Entity.Payload = "Changed secondary order";

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.False(
            entry.Property(value => value.Payload)
                .IsModified);
    }

    /// <summary>Notification strategies without an ordering original still guard its current value.</summary>
    /// <param name="change">Whether the current value changes after the candidate snapshot.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthorizedNotificationCandidateDoesNotRequireOrderingOriginal(
        bool change
    )
    {
        // Arrange
        await using var context = new NotificationContext();
        var node = CreateNode();
        context.Attach(node);
        node.Name = "Bravo";
        var entry = context.Entry(node);
        var update = UpdateEntry(context, node);
        var plan = CapturePlan([entry]);

        if (change)
        {
            node.Name = "Charlie";
        }

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.Equal(change, changed);
        Assert.False(update.CanHaveOriginalValue(entry.Metadata.FindProperty(nameof(ProbeNode.Name))!));
        Assert.True(
            entry.Property(value => value.Name)
                .IsModified);
    }

    /// <summary>Shadow role changes are read through EF rather than through an unrelated CLR member.</summary>
    [Fact]
    public async Task AuthorizedCandidateRejectsUnmarkedShadowRoleChange()
    {
        // Arrange
        await using var context = new ShadowPositionPlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var property = entry
            .Property<long>("ShadowPosition")
            .Metadata;

        var update = UpdateEntry(context, entry.Entity);
        var plan = CapturePlan([entry]);
        NestedSetInsertionContract.Instance.SetProperty(update, property, 7L);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(
            7L,
            entry.Property<long>("ShadowPosition")
                .CurrentValue);

        Assert.False(
            entry.Property<long>("ShadowPosition")
                .IsModified);

        Assert.Equal(0, entry.Entity.Position);
    }

    /// <summary>The candidate snapshot reads provider-generated sidecars independently of the CLR key.</summary>
    [Fact]
    public async Task AuthorizedCandidateRejectsChangedGeneratedSidecar()
    {
        // Arrange
        await using var context = new GeneratedNodePlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = UpdateEntry(context, entry.Entity);
        var property = entry
            .Property(value => value.Id)
            .Metadata;

        update.SetStoreGeneratedValue(property, 1, setModified: false);
        var plan = CapturePlan([entry]);
        update.SetStoreGeneratedValue(property, 2, setModified: false);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.Equal(1, entry.Entity.Id);
        Assert.Equal(2, update.GetCurrentValue<int>(property));
        Assert.False(update.IsModified(property));
    }

    /// <summary>A generated sidecar cannot hide a later raw CLR key edit from the authorized plan.</summary>
    [Fact]
    public async Task AuthorizedCandidateRejectsClrKeyChangeBehindGeneratedSidecar()
    {
        // Arrange
        await using var context = new GeneratedNodePlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = UpdateEntry(context, entry.Entity);
        var property = entry
            .Property(value => value.Id)
            .Metadata;

        update.SetStoreGeneratedValue(property, 1, setModified: false);
        var plan = CapturePlan([entry]);
        entry.Entity.Id = 2;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.Equal(1, update.GetCurrentValue<int>(property));
        Assert.Equal(2, entry.Entity.Id);
        Assert.False(update.IsModified(property));
        Assert.True(changed);
    }

    /// <summary>Initially different CLR and generated values remain valid when neither value changes.</summary>
    [Fact]
    public async Task AuthorizedCandidatePreservesCapturedGeneratedDivergence()
    {
        // Arrange
        await using var context = new GeneratedNodePlanContext();
        var entry = context.Attach(CreateNode());
        entry.Property(value => value.Name)
            .IsModified = true;

        var update = UpdateEntry(context, entry.Entity);
        var property = entry
            .Property(value => value.Id)
            .Metadata;

        update.SetStoreGeneratedValue(property, 2, setModified: false);
        var plan = CapturePlan([entry]);

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.False(changed);
        Assert.Equal(1, entry.Entity.Id);
        Assert.Equal(2, update.GetCurrentValue<int>(property));
        Assert.False(update.IsModified(property));
    }

    /// <summary>The ordinary observer also detects raw CLR keys hidden behind an unchanged generated sidecar.</summary>
    [Fact]
    public async Task OrdinaryObserverRejectsClrKeyChangeBehindGeneratedSidecar()
    {
        // Arrange
        await using var context = new GeneratedNodePlanContext();
        var node = CreateNode();
        var entry = context.Attach(node);
        var update = UpdateEntry(context, node);
        var property = entry
            .Property(value => value.Id)
            .Metadata;

        update.SetStoreGeneratedValue(property, 1, setModified: false);
        node.Id = 2;

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.Equal(1, update.GetCurrentValue<int>(property));
        Assert.Equal(1, update.GetOriginalValue<int>(property));
        Assert.Equal(2, node.Id);
        Assert.False(update.IsModified(property));
        Assert.True(changed);
    }

    /// <summary>Mutable ordering snapshots own independent current and original values.</summary>
    /// <param name="original">Whether to mutate EF's original array instead of the current CLR array.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthorizedCandidateClonesMutableOrderingSnapshots(
        bool original
    )
    {
        // Arrange
        await using var context = new MutableOrderingPlanContext();
        var node = new MutablePlanNode
        {
            Id = 1,
            TreeId = Guid.NewGuid(),
            Left = 1,
            Right = 2,
            Rank = [1, 2],
        };

        var entry = context.Attach(node);
        entry.Property(value => value.Rank)
            .IsModified = true;

        var plan = CapturePlan([entry]);
        var values = original
            ? entry.Property(value => value.Rank)
                .OriginalValue
            : node.Rank;

        values[0] = 9;

        // Act
        var changed = PlanHasChanges(context, plan);

        // Assert
        Assert.True(changed);
        Assert.True(
            entry.Property(value => value.Rank)
                .IsModified);

        Assert.Equal(
            original
                ? (byte)1
                : (byte)9,
            node.Rank[0]);
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
