#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups.Migrations.Sqlite;

/// <summary>Creates the complete tenant-scoped user and group model.</summary>
public partial class InitialUserGroups : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DokaNestedSetTrees_15640E0F343786E5",
            columns: table => new
            {
                Scope = table.Column<Guid>(type: "TEXT", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Lifecycle = table.Column<byte>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DokaNestedSetTrees_15640E0F343786E5", x => new { x.Scope, x.TreeId });
            });

        migrationBuilder.CreateTable(
            name: "DokaNestedSetTrees_CD53048DAAB2A93A",
            columns: table => new
            {
                Scope = table.Column<Guid>(type: "TEXT", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Lifecycle = table.Column<byte>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DokaNestedSetTrees_CD53048DAAB2A93A", x => new { x.Scope, x.TreeId });
            });

        migrationBuilder.CreateTable(
            name: "Privileges",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Privileges", x => new { x.TenantId, x.Code });
            });

        migrationBuilder.CreateTable(
            name: "Roles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Roles", x => new { x.TenantId, x.Id });
            });

        migrationBuilder.CreateTable(
            name: "UserGroups",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false),
                ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Left = table.Column<long>(type: "INTEGER", nullable: false),
                Right = table.Column<long>(type: "INTEGER", nullable: false),
                Depth = table.Column<int>(type: "INTEGER", nullable: false),
                Position = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserGroups", x => x.Id);
                table.UniqueConstraint("AK_UserGroups_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_NestedSet_DepthMin_FDB24289E574", "\"Depth\" >= 0");
                table.CheckConstraint("CK_NestedSet_LeftMin_BD623D388EE3", "\"Left\" >= 1");
                table.CheckConstraint("CK_NestedSet_PositionMin_27B996FE9A42", "\"Position\" >= 0");
                table.CheckConstraint("CK_NestedSet_RightAfterLeft_11FBC0D9D1D1", "\"Right\" > \"Left\"");
                table.ForeignKey(
                    name: "FK_UserGroups_UserGroups_TenantId_ParentId",
                    columns: x => new { x.TenantId, x.ParentId },
                    principalTable: "UserGroups",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                SupervisorId = table.Column<int>(type: "INTEGER", nullable: true),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Position = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Depth = table.Column<int>(type: "INTEGER", nullable: false),
                Left = table.Column<long>(type: "INTEGER", nullable: false),
                NestedSetPosition = table.Column<long>(type: "INTEGER", nullable: false),
                Right = table.Column<long>(type: "INTEGER", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
                table.UniqueConstraint("AK_Users_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_NestedSet_DepthMin_F03A5F16EC38", "\"Depth\" >= 0");
                table.CheckConstraint("CK_NestedSet_LeftMin_F767115F474D", "\"Left\" >= 1");
                table.CheckConstraint("CK_NestedSet_PositionMin_9BF8936D09D9", "\"NestedSetPosition\" >= 0");
                table.CheckConstraint("CK_NestedSet_RightAfterLeft_027293AE25EF", "\"Right\" > \"Left\"");
                table.ForeignKey(
                    name: "FK_Users_Users_TenantId_SupervisorId",
                    columns: x => new { x.TenantId, x.SupervisorId },
                    principalTable: "Users",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "RolePrivileges",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                PrivilegeCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RolePrivileges", x => new { x.TenantId, x.RoleId, x.PrivilegeCode });
                table.ForeignKey(
                    name: "FK_RolePrivileges_Privileges_TenantId_PrivilegeCode",
                    columns: x => new { x.TenantId, x.PrivilegeCode },
                    principalTable: "Privileges",
                    principalColumns: ["TenantId", "Code"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_RolePrivileges_Roles_TenantId_RoleId",
                    columns: x => new { x.TenantId, x.RoleId },
                    principalTable: "Roles",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserGroupRoles",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserGroupRoles", x => new { x.TenantId, x.UserGroupId, x.RoleId });
                table.ForeignKey(
                    name: "FK_UserGroupRoles_Roles_TenantId_RoleId",
                    columns: x => new { x.TenantId, x.RoleId },
                    principalTable: "Roles",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserGroupRoles_UserGroups_TenantId_UserGroupId",
                    columns: x => new { x.TenantId, x.UserGroupId },
                    principalTable: "UserGroups",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserGroupMemberships",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                UserGroupId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserGroupMemberships", x => new { x.TenantId, x.UserId, x.UserGroupId });
                table.ForeignKey(
                    name: "FK_UserGroupMemberships_UserGroups_TenantId_UserGroupId",
                    columns: x => new { x.TenantId, x.UserGroupId },
                    principalTable: "UserGroups",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserGroupMemberships_Users_TenantId_UserId",
                    columns: x => new { x.TenantId, x.UserId },
                    principalTable: "Users",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RolePrivileges_TenantId_PrivilegeCode",
            table: "RolePrivileges",
            columns: ["TenantId", "PrivilegeCode"]);

        migrationBuilder.CreateIndex(
            name: "IX_UserGroupMemberships_TenantId_UserGroupId_UserId",
            table: "UserGroupMemberships",
            columns: ["TenantId", "UserGroupId", "UserId"]);

        migrationBuilder.CreateIndex(
            name: "IX_UserGroupRoles_TenantId_RoleId",
            table: "UserGroupRoles",
            columns: ["TenantId", "RoleId"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_3ACD4966042D5AD04BBA37F6",
            table: "UserGroups",
            columns: ["TenantId", "TreeId", "Left"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_69CAAEEC5F26619C81245722",
            table: "UserGroups",
            columns: ["TenantId", "TreeId", "Right"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_B4EBB8A364B521E6D7E941EB",
            table: "UserGroups",
            columns: ["TenantId", "TreeId", "ParentId", "Position"]);

        migrationBuilder.CreateIndex(
            name: "IX_UserGroups_TenantId_ParentId",
            table: "UserGroups",
            columns: ["TenantId", "ParentId"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_001BCAF47EB672AE55D4A418",
            table: "Users",
            columns: ["TenantId", "TreeId", "Left"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_38E1F10E9189DD3005AFB54E",
            table: "Users",
            columns: ["TenantId", "TreeId", "SupervisorId", "NestedSetPosition"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_CEAC656CDF8D8E7FF6AE0A82",
            table: "Users",
            columns: ["TenantId", "TreeId", "Right"]);

        migrationBuilder.CreateIndex(
            name: "IX_Users_TenantId_SupervisorId",
            table: "Users",
            columns: ["TenantId", "SupervisorId"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DokaNestedSetTrees_15640E0F343786E5");

        migrationBuilder.DropTable(
            name: "DokaNestedSetTrees_CD53048DAAB2A93A");

        migrationBuilder.DropTable(
            name: "RolePrivileges");

        migrationBuilder.DropTable(
            name: "UserGroupMemberships");

        migrationBuilder.DropTable(
            name: "UserGroupRoles");

        migrationBuilder.DropTable(
            name: "Privileges");

        migrationBuilder.DropTable(
            name: "Users");

        migrationBuilder.DropTable(
            name: "Roles");

        migrationBuilder.DropTable(
            name: "UserGroups");
    }
}
