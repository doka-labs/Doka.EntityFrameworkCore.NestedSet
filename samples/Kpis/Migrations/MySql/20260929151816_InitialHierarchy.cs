#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis.Migrations.MySql;

/// <summary>Creates the Doka hierarchy tables, registry, constraints, and indexes.</summary>
public partial class InitialHierarchy : Migration
{
    /// <inheritdoc />
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder.AlterDatabase(
            collation: "utf8mb4_bin");

        migrationBuilder.CreateTable(
            name: "DokaNestedSetTrees_21E2E7888A7A36B0",
            columns: table => new
            {
                Scope = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false),
                TreeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false),
                Lifecycle = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DokaNestedSetTrees_21E2E7888A7A36B0", x => new { x.Scope, x.TreeId });
            });

        migrationBuilder.CreateTable(
            name: "Kpis",
            columns: table => new
            {
                NodeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Title = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                SuccessfulDeployments = table.Column<int>(type: "int", nullable: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ProjectId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                TreeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ParentMetricId = table.Column<Guid>(
                    type: "binary(16)",
                    fixedLength: true,
                    maxLength: 16,
                    nullable: true
                )
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Start = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                End = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Level = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                SiblingPosition = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Kpis", x => x.NodeId);
                table.UniqueConstraint("AK_Kpis_ProjectId_NodeId", x => new { x.ProjectId, x.NodeId });
                table.CheckConstraint("CK_NestedSet_DepthMin_1E494704C6EE", "`Level` >= 0");
                table.CheckConstraint("CK_NestedSet_LeftMin_B61E76BF6470", "`Start` >= 1");
                table.CheckConstraint("CK_NestedSet_PositionMin_9582FD621114", "`SiblingPosition` >= 0");
                table.CheckConstraint("CK_NestedSet_RightAfterLeft_F0F157FAE1F9", "`End` > `Start`");
                table.ForeignKey(
                    name: "FK_Kpis_Kpis_ProjectId_ParentMetricId",
                    columns: x => new { x.ProjectId, x.ParentMetricId },
                    principalTable: "Kpis",
                    principalColumns: ["ProjectId", "NodeId"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Kpis_ProjectId_ParentMetricId",
            table: "Kpis",
            columns: ["ProjectId", "ParentMetricId"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_0D3239B382B789AD6F29E1E1",
            table: "Kpis",
            columns: ["ProjectId", "TreeId", "Start"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_654C58B4548D52B9D3D2FF9B",
            table: "Kpis",
            columns: ["ProjectId", "TreeId", "End"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_BA98001BADCB447C9F23278A",
            table: "Kpis",
            columns: ["ProjectId", "TreeId", "ParentMetricId", "SiblingPosition"]);
    }

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder.DropTable(
            name: "DokaNestedSetTrees_21E2E7888A7A36B0");

        migrationBuilder.DropTable(
            name: "Kpis");
    }
}
