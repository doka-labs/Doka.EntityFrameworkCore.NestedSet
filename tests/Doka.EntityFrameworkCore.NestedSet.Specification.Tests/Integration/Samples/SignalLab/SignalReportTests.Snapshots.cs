using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using Microsoft.Data.Sqlite;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Proves the same callback used by scalar profiles sees actual node materialization.</summary>
    [Fact]
    public async Task MaterializationCounterObservesAnActualNode()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        probe.Reset();

        // Act
        var node = await context.Nodes.AsNoTracking().SingleAsync(value => value.TenantId == data.TenantId
            && value.Id == SignalReportDataset.Id(4), CancellationToken.None);

        // Assert
        Assert.Equal("Sensor A", node.Name);
        Assert.Equal(1, probe.Entities);
        Assert.Single(probe.Readers);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Already-bound tree and tenant isolation cannot be widened by lookalike fact identities.</summary>
    [Fact]
    public async Task LookalikeTreesAndTenantsDoNotContributeToTheSelectedReport()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var request = SingleAnchor(data) with { Criteria = [] };
        var rootBounds = await context.Nodes.Where(node => node.TenantId == data.TenantId
                && (node.Id == SignalReportDataset.Id(1) || node.Id == SignalReportDataset.Id(10)))
            .Select(node => new
            {
                Left = EF.Property<long>(node, SignalStorage.Left),
                Right = EF.Property<long>(node, SignalStorage.Right),
            }).ToArrayAsync(CancellationToken.None);

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(2, rootBounds.Length);
        Assert.All(rootBounds, bounds =>
        {
            Assert.Equal(1, bounds.Left);
            Assert.Equal(10, bounds.Right);
        });

        Assert.Equal(70, Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>A caller's serializable boundary stays open and owns the report's complete shared view.</summary>
    [Fact]
    public async Task ReportPreservesAnExistingSerializableTransaction()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var request = SingleAnchor(data);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            CancellationToken.None);

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(70, Assert.Single(cells).Value);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// Weak caller isolation is rejected before catalog or aggregate I/O rather than weakening the promised view.
    /// </summary>
    [EngineFact(ExcludedEngines = ["Sqlite"],
        Reason = "SQLite exposes Serializable or ReadUncommitted rather than a ReadCommitted transaction.")]
    public async Task ReportRejectsAReadCommittedCallerBeforeReading()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var request = SingleAnchor(data);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,
            CancellationToken.None);

        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportBuilder.ReadAsync(context,
            request,
            CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Empty(probe.Readers);
    }

    /// <summary>
    /// Catalog and aggregate never combine old classification data with a newly moved and updated branch.
    /// </summary>
    [Fact]
    public async Task ReportSnapshotKeepsStructurePeriodsMetadataAndFactsFromOneView()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        await using var writer = await _fixture.CreateContextAsync<SignalLabContext>(Engine,
            static options => new SignalLabContext(options));

        var request = SingleAnchor(data);
        var committed = false;
        SqliteTransaction? deferred = null;

        if (Engine == "Sqlite")
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL", CancellationToken.None);
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            // ReSharper disable once MethodHasAsyncOverload
            // WHY: Microsoft.Data.Sqlite exposes deferred transaction selection only on this synchronous overload.
            // The caller-owned WAL read view lets the staged writer commit without requesting a write lock here.
            deferred = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
        }

        await using var native = deferred;
        await using var enlisted = native is null
            ? null
            : await context.Database.UseTransactionAsync(native, CancellationToken.None);

        probe.Reset();
        probe.AfterFirstReader = async token =>
        {
            // WHY: Registry-coordinated server writes require ReadCommitted; SQLite uses its Serializable write
            // boundary.
            var writeIsolation = Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted;
            await using var transaction = await writer.Database.BeginTransactionAsync(writeIsolation, token);
            await writer.NestedSet<SignalNode>().ForScope(data.TenantId)
                .MoveToAsync(SignalReportDataset.Id(4), SignalReportDataset.Id(10), token);
            await writer.LinkAvailability.Where(period => period.TenantId == data.TenantId
                && period.ParentId == SignalReportDataset.Id(3) && period.ChildId == SignalReportDataset.Id(4))
                .ExecuteDeleteAsync(token);
            writer.LinkAvailability.Add(new LinkWindow
            {
                TenantId = data.TenantId,
                ParentId = SignalReportDataset.Id(10),
                ChildId = SignalReportDataset.Id(4),
                FromDate = SignalReportDataset.FirstDay,
            });
            await writer.TagValues.Where(value => value.TenantId == data.TenantId
                && value.NodeId == SignalReportDataset.Id(4) && value.FieldCode == "RackCode")
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, "R9"), token);
            await writer.PulseRecords.Where(value => value.TenantId == data.TenantId
                && value.NodeId == SignalReportDataset.Id(4))
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.PulseCount, 100), token);
            await writer.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            committed = true;
        };

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.True(committed);
        Assert.Equal(70, Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// Deferred report construction never opens the connection, loads anchors or materializes measurements.
    /// </summary>
    [Fact]
    public async Task BuildPerformsNoIoAndDoesNotMaterializeDomainEntities()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var request = SignalReportValidation.Snapshot(data.Request());
        probe.Reset();

        // Act
        var query = SignalReportBuilder.Build(context, request);

        // Assert
        Assert.Same(((IQueryable<SignalNode>)context.Nodes).Provider, query.Provider);
        Assert.Empty(probe.Readers);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
