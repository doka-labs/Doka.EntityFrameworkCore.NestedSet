#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups.Migrations.MySql;

/// <summary>
/// Adds classified group searches, explicit person roles, typed routing values, and direct-edge validity.
/// </summary>
public partial class GroupSupervisorQueries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // WHY: Existing application rows remain visible and equally ranked. Doka requires the application
        // to choose these replacement values explicitly instead of inventing data for required columns.
        migrationBuilder.AddColumn<bool>(
            name: "Visible",
            table: "Users",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true)
            .Annotation("Doka:MySql:RequiresExplicitBackfill", true)
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.AddColumn<int>(
            name: "TypeId",
            table: "UserGroups",
            type: "int",
            nullable: true)
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.AddColumn<bool>(
            name: "Visible",
            table: "UserGroups",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true)
            .Annotation("Doka:MySql:RequiresExplicitBackfill", true)
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.AddColumn<DateOnly>(
            name: "FromDate",
            table: "UserGroupMemberships",
            type: "date",
            nullable: true)
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.AddColumn<int>(
            name: "LeadershipPriority",
            table: "UserGroupMemberships",
            type: "int",
            nullable: false,
            defaultValue: 0)
            .Annotation("Doka:MySql:RequiresExplicitBackfill", true)
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ToDate",
            table: "UserGroupMemberships",
            type: "date",
            nullable: true)
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.AddColumn<bool>(
            name: "Visible",
            table: "UserGroupMemberships",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true)
            .Annotation("Doka:MySql:RequiresExplicitBackfill", true)
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.AddColumn<bool>(
            name: "Visible",
            table: "Roles",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true)
            .Annotation("Doka:MySql:RequiresExplicitBackfill", true)
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.CreateTable(
            name: "GroupRelationPeriods",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ParentId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ChildId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FromDate = table.Column<DateOnly>(type: "date", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ToDate = table.Column<DateOnly>(type: "date", nullable: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupRelationPeriods", x => new { x.TenantId, x.ParentId, x.ChildId, x.FromDate });
                table.ForeignKey(
                    name: "FK_GroupRelationPeriods_UserGroups_TenantId_ChildId",
                    columns: x => new { x.TenantId, x.ChildId },
                    principalTable: "UserGroups",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_GroupRelationPeriods_UserGroups_TenantId_ParentId",
                    columns: x => new { x.TenantId, x.ParentId },
                    principalTable: "UserGroups",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "GroupTypes",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupTypes", x => new { x.TenantId, x.Id });
            });

        migrationBuilder.CreateTable(
            name: "MembershipRoles",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                UserId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                UserGroupId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                RoleId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Visible = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FromDate = table.Column<DateOnly>(type: "date", nullable: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ToDate = table.Column<DateOnly>(type: "date", nullable: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MembershipRoles", x => new { x.TenantId, x.UserId, x.UserGroupId, x.RoleId });
                table.ForeignKey(
                    name: "FK_MembershipRoles_Roles_TenantId_RoleId",
                    columns: x => new { x.TenantId, x.RoleId },
                    principalTable: "Roles",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MembershipRoles_UserGroupMemberships_TenantId_UserId_UserGro~",
                    columns: x => new { x.TenantId, x.UserId, x.UserGroupId },
                    principalTable: "UserGroupMemberships",
                    principalColumns: ["TenantId", "UserId", "UserGroupId"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserMetadataDefinitions",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(
                    type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ValueKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserMetadataDefinitions", x => new { x.TenantId, x.FieldCode });
            });

        migrationBuilder.CreateTable(
            name: "UserRoles",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                UserId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                RoleId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Visible = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FromDate = table.Column<DateOnly>(type: "date", nullable: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ToDate = table.Column<DateOnly>(type: "date", nullable: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserRoles", x => new { x.TenantId, x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_UserRoles_Roles_TenantId_RoleId",
                    columns: x => new { x.TenantId, x.RoleId },
                    principalTable: "Roles",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserRoles_Users_TenantId_UserId",
                    columns: x => new { x.TenantId, x.UserId },
                    principalTable: "Users",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "GroupMetadataDefinitions",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(
                    type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                GroupTypeId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ValueKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupMetadataDefinitions", x => new { x.TenantId, x.GroupTypeId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_GroupMetadataDefinitions_GroupTypes_TenantId_GroupTypeId",
                    columns: x => new { x.TenantId, x.GroupTypeId },
                    principalTable: "GroupTypes",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MembershipMetadataDefinitions",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(
                    type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                GroupTypeId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ValueKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_MembershipMetadataDefinitions", x => new { x.TenantId, x.GroupTypeId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_MembershipMetadataDefinitions_GroupTypes_TenantId_GroupTypeId",
                    columns: x => new { x.TenantId, x.GroupTypeId },
                    principalTable: "GroupTypes",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserMetadatas",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                UserId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(
                    type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Value = table.Column<string>(
                    type: "varchar(120)", maxLength: 120, nullable: true, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserMetadatas", x => new { x.TenantId, x.UserId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_UserMetadatas_UserMetadataDefinitions_TenantId_FieldCode",
                    columns: x => new { x.TenantId, x.FieldCode },
                    principalTable: "UserMetadataDefinitions",
                    principalColumns: ["TenantId", "FieldCode"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserMetadatas_Users_TenantId_UserId",
                    columns: x => new { x.TenantId, x.UserId },
                    principalTable: "Users",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "GroupMetadatas",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                UserGroupId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(
                    type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                GroupTypeId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Value = table.Column<string>(
                    type: "varchar(120)", maxLength: 120, nullable: true, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupMetadatas", x => new { x.TenantId, x.UserGroupId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_GroupMetadatas_GroupMetadataDefinitions_TenantId_GroupTypeId~",
                    columns: x => new { x.TenantId, x.GroupTypeId, x.FieldCode },
                    principalTable: "GroupMetadataDefinitions",
                    principalColumns: ["TenantId", "GroupTypeId", "FieldCode"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_GroupMetadatas_UserGroups_TenantId_UserGroupId",
                    columns: x => new { x.TenantId, x.UserGroupId },
                    principalTable: "UserGroups",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MembershipMetadatas",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                UserId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                UserGroupId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(
                    type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                GroupTypeId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Value = table.Column<string>(
                    type: "varchar(120)", maxLength: 120, nullable: true, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_MembershipMetadatas", x => new { x.TenantId, x.UserId, x.UserGroupId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_MembershipMetadatas_MembershipMetadataDefinitions_TenantId_G~",
                    columns: x => new { x.TenantId, x.GroupTypeId, x.FieldCode },
                    principalTable: "MembershipMetadataDefinitions",
                    principalColumns: ["TenantId", "GroupTypeId", "FieldCode"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MembershipMetadatas_UserGroupMemberships_TenantId_UserId_Use~",
                    columns: x => new { x.TenantId, x.UserId, x.UserGroupId },
                    principalTable: "UserGroupMemberships",
                    principalColumns: ["TenantId", "UserId", "UserGroupId"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserGroups_TenantId_TypeId",
            table: "UserGroups",
            columns: ["TenantId", "TypeId"]);

        migrationBuilder.CreateIndex(
            name: "IX_GroupMetadatas_TenantId_GroupTypeId_FieldCode",
            table: "GroupMetadatas",
            columns: ["TenantId", "GroupTypeId", "FieldCode"]);

        migrationBuilder.CreateIndex(
            name: "IX_GroupRelationPeriods_TenantId_ChildId",
            table: "GroupRelationPeriods",
            columns: ["TenantId", "ChildId"]);

        migrationBuilder.CreateIndex(
            name: "IX_MembershipMetadatas_TenantId_GroupTypeId_FieldCode",
            table: "MembershipMetadatas",
            columns: ["TenantId", "GroupTypeId", "FieldCode"]);

        migrationBuilder.CreateIndex(
            name: "IX_MembershipRoles_TenantId_RoleId",
            table: "MembershipRoles",
            columns: ["TenantId", "RoleId"]);

        migrationBuilder.CreateIndex(
            name: "IX_UserMetadatas_TenantId_FieldCode",
            table: "UserMetadatas",
            columns: ["TenantId", "FieldCode"]);

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_TenantId_RoleId",
            table: "UserRoles",
            columns: ["TenantId", "RoleId"]);

        migrationBuilder.AddForeignKey(
            name: "FK_UserGroups_GroupTypes_TenantId_TypeId",
            table: "UserGroups",
            columns: ["TenantId", "TypeId"],
            principalTable: "GroupTypes",
            principalColumns: ["TenantId", "Id"],
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_UserGroups_GroupTypes_TenantId_TypeId",
            table: "UserGroups");

        migrationBuilder.DropTable(
            name: "GroupMetadatas");

        migrationBuilder.DropTable(
            name: "GroupRelationPeriods");

        migrationBuilder.DropTable(
            name: "MembershipMetadatas");

        migrationBuilder.DropTable(
            name: "MembershipRoles");

        migrationBuilder.DropTable(
            name: "UserMetadatas");

        migrationBuilder.DropTable(
            name: "UserRoles");

        migrationBuilder.DropTable(
            name: "GroupMetadataDefinitions");

        migrationBuilder.DropTable(
            name: "MembershipMetadataDefinitions");

        migrationBuilder.DropTable(
            name: "UserMetadataDefinitions");

        migrationBuilder.DropTable(
            name: "GroupTypes");

        migrationBuilder.DropIndex(
            name: "IX_UserGroups_TenantId_TypeId",
            table: "UserGroups");

        migrationBuilder.DropColumn(
            name: "Visible",
            table: "Users")
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.DropColumn(
            name: "TypeId",
            table: "UserGroups")
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.DropColumn(
            name: "Visible",
            table: "UserGroups")
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.DropColumn(
            name: "FromDate",
            table: "UserGroupMemberships")
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.DropColumn(
            name: "LeadershipPriority",
            table: "UserGroupMemberships")
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.DropColumn(
            name: "ToDate",
            table: "UserGroupMemberships")
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.DropColumn(
            name: "Visible",
            table: "UserGroupMemberships")
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);

        migrationBuilder.DropColumn(
            name: "Visible",
            table: "Roles")
            .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None);
    }
}
