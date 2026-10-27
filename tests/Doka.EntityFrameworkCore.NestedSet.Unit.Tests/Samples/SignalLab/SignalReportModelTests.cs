using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

/// <summary>Guards the actual sample's model boundaries independently of running a database.</summary>
public sealed class SignalReportModelTests
{
    /// <summary>All structural coordinates remain shadow state and normal parent writes cannot cross tenants.</summary>
    [Fact]
    public void ReportingNodeKeepsStructureInShadowStateAndTenantQualifiedRelationships()
    {
        // Arrange
        using var context = CreateSqliteContext();

        // Act
        var node = context.Model.FindEntityType(typeof(SignalNode))!;

        // Assert
        string[] roles = [SignalStorage.TreeId, SignalStorage.ParentId, SignalStorage.Left,
            SignalStorage.Right, SignalStorage.Depth, SignalStorage.Position];

        Assert.All(roles, role =>
        {
            Assert.True(node.FindProperty(role)!.IsShadowProperty());
            Assert.Null(typeof(SignalNode).GetProperty(role));
        });
        Assert.Equal([nameof(SignalNode.TenantId), nameof(SignalNode.Id)],
            node.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(node.GetForeignKeys(), key => key.PrincipalEntityType == node
            && key.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(SignalNode.TenantId), SignalStorage.ParentId])
            && key.DeleteBehavior == DeleteBehavior.Restrict);
    }

    /// <summary>Each acquisition format owns independent event identities without an EF inheritance mapping.</summary>
    [Fact]
    public void PhysicalFactSourcesDoNotBecomeAnEfInheritanceHierarchy()
    {
        // Arrange
        using var context = CreateSqliteContext();
        Type[] sources = [typeof(PulseRecord), typeof(SweepRecord), typeof(BurstRecord)];

        // Act
        var mappings = sources.Select(source => context.Model.FindEntityType(source)!).ToArray();

        // Assert
        Assert.Equal(3, mappings.Select(mapping => mapping.GetTableName()).Distinct().Count());
        Assert.All(mappings, mapping =>
        {
            Assert.Null(mapping.BaseType);
            Assert.Equal([nameof(PulseRecord.Sequence)],
                mapping.FindPrimaryKey()!.Properties.Select(property => property.Name));
            Assert.Equal(ValueGenerated.OnAdd, mapping.FindProperty(nameof(PulseRecord.Sequence))!.ValueGenerated);
        });
    }

    /// <summary>Normal entity-configuration refactoring does not leave an unrecorded sample schema change.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportingModelMatchesItsCheckedInMigrationSnapshot(bool doka)
    {
        // Arrange
        var dokaOptions = new DbContextOptionsBuilder<SignalLabContext>()
            .UseMySql("Server=localhost;Database=unused;User ID=unused;Password=unused",
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)), provider => provider.UseNestedSets());

        var sqliteOptions = new DbContextOptionsBuilder<SignalLabSqliteContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());

        using var services = SampleTestServices.Create(doka ? dokaOptions.Options : sqliteOptions.Options);
        using var context = doka
            ? new SignalLabContext(dokaOptions.UseInternalServiceProvider(services).Options)
            : new SignalLabSqliteContext(sqliteOptions.UseInternalServiceProvider(services).Options);

        // Act
        var pending = context.Database.HasPendingModelChanges();

        // Assert
        Assert.False(pending);
    }

    /// <summary>Constructs the same model without connecting, creating tables or invoking migration I/O.</summary>
    private static SignalLabSqliteContext CreateSqliteContext() => new(new DbContextOptionsBuilder<SignalLabSqliteContext>()
        .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()).Options);
}
