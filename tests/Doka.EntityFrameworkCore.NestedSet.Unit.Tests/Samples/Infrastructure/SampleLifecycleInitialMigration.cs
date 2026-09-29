namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Creates one sentinel table through the public EF migration path used by the actual samples.</summary>
[DbContext(typeof(SampleLifecycleContext))]
[Migration("20260929000000_SampleLifecycleInitial")]
public sealed class SampleLifecycleInitialMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder.CreateTable(
            "SampleSentinels",
            columns: table => new
            {
                Id = table.Column<int>("INTEGER", nullable: false),
                Value = table.Column<string>("TEXT", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_SampleSentinels", sentinel => sentinel.Id));
    }

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable("SampleSentinels");

    /// <inheritdoc />
    protected override void BuildTargetModel(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
        SampleLifecycleContext.ConfigureModel(modelBuilder);
    }
}
