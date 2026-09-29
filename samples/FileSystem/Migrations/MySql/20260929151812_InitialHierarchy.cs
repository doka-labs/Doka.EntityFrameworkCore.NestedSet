#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem.Migrations.MySql;

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
            name: "DokaNestedSetTrees_CE8408B3FDD298F9",
            columns: table => new
            {
                TreeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false),
                Lifecycle = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DokaNestedSetTrees_CE8408B3FDD298F9", x => x.TreeId);
            });

        migrationBuilder.CreateTable(
            name: "Folders",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.AutoIncrement),
                TreeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ParentId = table.Column<int>(type: "int", nullable: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Left = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Right = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Depth = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Position = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Folders", x => x.Id);
                table.CheckConstraint("CK_NestedSet_DepthMin_D08AE90B5434", "`Depth` >= 0");
                table.CheckConstraint("CK_NestedSet_LeftMin_C427995A3819", "`Left` >= 1");
                table.CheckConstraint("CK_NestedSet_PositionMin_702D41AA53EE", "`Position` >= 0");
                table.CheckConstraint("CK_NestedSet_RightAfterLeft_6201912CB08F", "`Right` > `Left`");
                table.ForeignKey(
                    name: "FK_Folders_Folders_ParentId",
                    column: x => x.ParentId,
                    principalTable: "Folders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Folders_ParentId",
            table: "Folders",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_A06121AD99644E6A50E54A72",
            table: "Folders",
            columns: ["TreeId", "Right"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_A40159EE3BAF9A41CFAB0644",
            table: "Folders",
            columns: ["TreeId", "ParentId", "Position"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_DF89A3C5B555AC38693139FE",
            table: "Folders",
            columns: ["TreeId", "ParentId", "Name", "Category", "Id"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_FC8D8DA800E92DF9B4480F36",
            table: "Folders",
            columns: ["TreeId", "Left"]);
    }

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder.DropTable(
            name: "DokaNestedSetTrees_CE8408B3FDD298F9");

        migrationBuilder.DropTable(
            name: "Folders");
    }
}
