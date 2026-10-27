#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.Migrations.Sqlite;

/// <inheritdoc />
public partial class InitialInstruments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DokaNestedSetTrees_6D3B959BCB7F6518",
            columns: table => new
            {
                Scope = table.Column<Guid>(type: "TEXT", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Lifecycle = table.Column<byte>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DokaNestedSetTrees_6D3B959BCB7F6518", x => new { x.Scope, x.TreeId });
            });

        migrationBuilder.CreateTable(
            name: "Instruments",
            columns: table => new
            {
                NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                PulseCount = table.Column<int>(type: "INTEGER", nullable: true),
                ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false),
                ParentInstrumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                Start = table.Column<long>(type: "INTEGER", nullable: false),
                End = table.Column<long>(type: "INTEGER", nullable: false),
                Level = table.Column<int>(type: "INTEGER", nullable: false),
                SiblingPosition = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Instruments", x => x.NodeId);
                table.UniqueConstraint("AK_Instruments_ProjectId_NodeId", x => new { x.ProjectId, x.NodeId });
                table.CheckConstraint("CK_NestedSet_DepthMin_6E2667315AB0", "\"Level\" >= 0");
                table.CheckConstraint("CK_NestedSet_LeftMin_A6A6243D76E5", "\"Start\" >= 1");
                table.CheckConstraint("CK_NestedSet_PositionMin_7DA2F8CEE032", "\"SiblingPosition\" >= 0");
                table.CheckConstraint("CK_NestedSet_RightAfterLeft_74D779933A65", "\"End\" > \"Start\"");
                table.ForeignKey(
                    name: "FK_Instruments_Instruments_ProjectId_ParentInstrumentId",
                    columns: x => new { x.ProjectId, x.ParentInstrumentId },
                    principalTable: "Instruments",
                    principalColumns: ["ProjectId", "NodeId"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Instruments_ProjectId_ParentInstrumentId",
            table: "Instruments",
            columns: ["ProjectId", "ParentInstrumentId"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_5AE92F76AAFA51D7927A1978",
            table: "Instruments",
            columns: ["ProjectId", "TreeId", "ParentInstrumentId", "SiblingPosition"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_86A7489E72B2FFD583E931F6",
            table: "Instruments",
            columns: ["ProjectId", "TreeId", "End"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_D909D51FF5DF260562A87434",
            table: "Instruments",
            columns: ["ProjectId", "TreeId", "Start"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DokaNestedSetTrees_6D3B959BCB7F6518");

        migrationBuilder.DropTable(
            name: "Instruments");
    }
}
