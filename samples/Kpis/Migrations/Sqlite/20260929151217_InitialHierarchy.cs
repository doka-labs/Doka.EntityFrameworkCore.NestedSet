#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.Kpis.Migrations.Sqlite;

/// <summary>Creates the SQLite hierarchy tables, registry, constraints, and indexes.</summary>
public partial class InitialHierarchy : Migration
{
    /// <inheritdoc />
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder.CreateTable(
            name: "DokaNestedSetTrees_21E2E7888A7A36B0",
            columns: table => new
            {
                Scope = table.Column<Guid>(type: "TEXT", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Lifecycle = table.Column<byte>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DokaNestedSetTrees_21E2E7888A7A36B0", x => new { x.Scope, x.TreeId });
            });

        migrationBuilder.CreateTable(
            name: "Kpis",
            columns: table => new
            {
                NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                SuccessfulDeployments = table.Column<int>(type: "INTEGER", nullable: true),
                ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false),
                ParentMetricId = table.Column<Guid>(type: "TEXT", nullable: true),
                Start = table.Column<long>(type: "INTEGER", nullable: false),
                End = table.Column<long>(type: "INTEGER", nullable: false),
                Level = table.Column<int>(type: "INTEGER", nullable: false),
                SiblingPosition = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Kpis", x => x.NodeId);
                table.UniqueConstraint("AK_Kpis_ProjectId_NodeId", x => new { x.ProjectId, x.NodeId });
                table.CheckConstraint("CK_NestedSet_DepthMin_1E494704C6EE", "\"Level\" >= 0");
                table.CheckConstraint("CK_NestedSet_LeftMin_B61E76BF6470", "\"Start\" >= 1");
                table.CheckConstraint("CK_NestedSet_PositionMin_9582FD621114", "\"SiblingPosition\" >= 0");
                table.CheckConstraint("CK_NestedSet_RightAfterLeft_F0F157FAE1F9", "\"End\" > \"Start\"");
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
