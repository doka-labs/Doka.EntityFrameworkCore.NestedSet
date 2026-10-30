#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups.Migrations.Sqlite;

/// <summary>
/// Adds classified group searches, explicit person roles, typed routing values, and direct-edge validity.
/// </summary>
public partial class GroupSupervisorQueries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "Visible",
            table: "Users",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<int>(
            name: "TypeId",
            table: "UserGroups",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Visible",
            table: "UserGroups",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "FromDate",
            table: "UserGroupMemberships",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "LeadershipPriority",
            table: "UserGroupMemberships",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ToDate",
            table: "UserGroupMemberships",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Visible",
            table: "UserGroupMemberships",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "Visible",
            table: "Roles",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateTable(
            name: "GroupRelationPeriods",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                ParentId = table.Column<int>(type: "INTEGER", nullable: false),
                ChildId = table.Column<int>(type: "INTEGER", nullable: false),
                FromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                ToDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
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
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupTypes", x => new { x.TenantId, x.Id });
            });

        migrationBuilder.CreateTable(
            name: "MembershipRoles",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                UserGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                Visible = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                FromDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                ToDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
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
                    name: "FK_MembershipRoles_UserGroupMemberships_TenantId_UserId_UserGroupId",
                    columns: x => new { x.TenantId, x.UserId, x.UserGroupId },
                    principalTable: "UserGroupMemberships",
                    principalColumns: ["TenantId", "UserId", "UserGroupId"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserMetadataDefinitions",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                ValueKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserMetadataDefinitions", x => new { x.TenantId, x.FieldCode });
            });

        migrationBuilder.CreateTable(
            name: "UserRoles",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                Visible = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                FromDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                ToDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
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
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                GroupTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                ValueKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
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
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                GroupTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                ValueKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
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
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                Value = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true, collation: "BINARY")
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
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                GroupTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true, collation: "BINARY")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupMetadatas", x => new { x.TenantId, x.UserGroupId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_GroupMetadatas_GroupMetadataDefinitions_TenantId_GroupTypeId_FieldCode",
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
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                UserGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                GroupTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true, collation: "BINARY")
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_MembershipMetadatas", x => new { x.TenantId, x.UserId, x.UserGroupId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_MembershipMetadatas_MembershipMetadataDefinitions_TenantId_GroupTypeId_FieldCode",
                    columns: x => new { x.TenantId, x.GroupTypeId, x.FieldCode },
                    principalTable: "MembershipMetadataDefinitions",
                    principalColumns: ["TenantId", "GroupTypeId", "FieldCode"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MembershipMetadatas_UserGroupMemberships_TenantId_UserId_UserGroupId",
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
            table: "Users");

        migrationBuilder.DropColumn(
            name: "TypeId",
            table: "UserGroups");

        migrationBuilder.DropColumn(
            name: "Visible",
            table: "UserGroups");

        migrationBuilder.DropColumn(
            name: "FromDate",
            table: "UserGroupMemberships");

        migrationBuilder.DropColumn(
            name: "LeadershipPriority",
            table: "UserGroupMemberships");

        migrationBuilder.DropColumn(
            name: "ToDate",
            table: "UserGroupMemberships");

        migrationBuilder.DropColumn(
            name: "Visible",
            table: "UserGroupMemberships");

        migrationBuilder.DropColumn(
            name: "Visible",
            table: "Roles");
    }
}
