#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.Migrations.Sqlite;

/// <inheritdoc />
public partial class SignalAcquisition : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DokaNestedSetTrees_F2BE5E0377B809DD",
            columns: table => new
            {
                Scope = table.Column<Guid>(type: "TEXT", nullable: false),
                TreeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Lifecycle = table.Column<byte>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DokaNestedSetTrees_F2BE5E0377B809DD", x => new { x.Scope, x.TreeId });
            });

        migrationBuilder.CreateTable(
            name: "NodeKinds",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NodeKinds", x => new { x.TenantId, x.Id });
            });

        migrationBuilder.CreateTable(
            name: "ReportingNodes",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                NestedSetDepth = table.Column<int>(type: "INTEGER", nullable: false),
                NestedSetLeft = table.Column<long>(type: "INTEGER", nullable: false),
                NestedSetParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                NestedSetPosition = table.Column<long>(type: "INTEGER", nullable: false),
                NestedSetRight = table.Column<long>(type: "INTEGER", nullable: false),
                NestedSetTreeId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportingNodes", x => new { x.TenantId, x.Id });
                table.UniqueConstraint("AK_ReportingNodes_TenantId_TypeId_Id", x => new { x.TenantId, x.TypeId, x.Id });
                table.CheckConstraint("CK_NestedSet_DepthMin_706EF85AC4FF", "\"NestedSetDepth\" >= 0");
                table.CheckConstraint("CK_NestedSet_LeftMin_9C29CC100F5D", "\"NestedSetLeft\" >= 1");
                table.CheckConstraint("CK_NestedSet_PositionMin_5C730A1DDB5A", "\"NestedSetPosition\" >= 0");
                table.CheckConstraint("CK_NestedSet_RightAfterLeft_D817C86A9A56", "\"NestedSetRight\" > \"NestedSetLeft\"");
                table.ForeignKey(
                    name: "FK_ReportingNodes_NodeKinds_TenantId_TypeId",
                    columns: x => new { x.TenantId, x.TypeId },
                    principalTable: "NodeKinds",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ReportingNodes_ReportingNodes_TenantId_NestedSetParentId",
                    columns: x => new { x.TenantId, x.NestedSetParentId },
                    principalTable: "ReportingNodes",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SignalChannels",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MeasurementTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                UnitCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SignalChannels", x => new { x.TenantId, x.Id });
                table.ForeignKey(
                    name: "FK_SignalChannels_NodeKinds_TenantId_MeasurementTypeId",
                    columns: x => new { x.TenantId, x.MeasurementTypeId },
                    principalTable: "NodeKinds",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TagDefinitions",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                TypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                Label = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagDefinitions", x => new { x.TenantId, x.TypeId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_TagDefinitions_NodeKinds_TenantId_TypeId",
                    columns: x => new { x.TenantId, x.TypeId },
                    principalTable: "NodeKinds",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LinkAvailability",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                ParentId = table.Column<Guid>(type: "TEXT", nullable: false),
                ChildId = table.Column<Guid>(type: "TEXT", nullable: false),
                FromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                ToDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LinkAvailability", x => new { x.TenantId, x.ParentId, x.ChildId, x.FromDate });
                table.CheckConstraint("CK_LinkAvailability_OrderedDays", "\"ToDate\" IS NULL OR \"ToDate\" >= \"FromDate\"");
                table.ForeignKey(
                    name: "FK_LinkAvailability_ReportingNodes_TenantId_ChildId",
                    columns: x => new { x.TenantId, x.ChildId },
                    principalTable: "ReportingNodes",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LinkAvailability_ReportingNodes_TenantId_ParentId",
                    columns: x => new { x.TenantId, x.ParentId },
                    principalTable: "ReportingNodes",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "BurstRecords",
            columns: table => new
            {
                Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                SignalId = table.Column<Guid>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                BucketSize = table.Column<int>(type: "INTEGER", nullable: false),
                CapturedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Magnitude = table.Column<double>(type: "REAL", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BurstRecords", x => x.Sequence);
                table.ForeignKey(
                    name: "FK_BurstRecords_ReportingNodes_TenantId_DeviceId",
                    columns: x => new { x.TenantId, x.DeviceId },
                    principalTable: "ReportingNodes",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BurstRecords_SignalChannels_TenantId_SignalId",
                    columns: x => new { x.TenantId, x.SignalId },
                    principalTable: "SignalChannels",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PulseRecords",
            columns: table => new
            {
                Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Granularity = table.Column<int>(type: "INTEGER", nullable: false),
                Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                PulseCount = table.Column<double>(type: "REAL", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PulseRecords", x => x.Sequence);
                table.ForeignKey(
                    name: "FK_PulseRecords_ReportingNodes_TenantId_NodeId",
                    columns: x => new { x.TenantId, x.NodeId },
                    principalTable: "ReportingNodes",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PulseRecords_SignalChannels_TenantId_ChannelId",
                    columns: x => new { x.TenantId, x.ChannelId },
                    principalTable: "SignalChannels",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SweepRecords",
            columns: table => new
            {
                Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                StreamId = table.Column<Guid>(type: "TEXT", nullable: false),
                ReceiverId = table.Column<Guid>(type: "TEXT", nullable: false),
                Resolution = table.Column<int>(type: "INTEGER", nullable: false),
                CompletedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                IntegratedTicks = table.Column<double>(type: "REAL", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SweepRecords", x => x.Sequence);
                table.ForeignKey(
                    name: "FK_SweepRecords_ReportingNodes_TenantId_ReceiverId",
                    columns: x => new { x.TenantId, x.ReceiverId },
                    principalTable: "ReportingNodes",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SweepRecords_SignalChannels_TenantId_StreamId",
                    columns: x => new { x.TenantId, x.StreamId },
                    principalTable: "SignalChannels",
                    principalColumns: ["TenantId", "Id"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TagChoices",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                TypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                Value = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "BINARY")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagChoices", x => new { x.TenantId, x.TypeId, x.FieldCode, x.Value });
                table.ForeignKey(
                    name: "FK_TagChoices_TagDefinitions_TenantId_TypeId_FieldCode",
                    columns: x => new { x.TenantId, x.TypeId, x.FieldCode },
                    principalTable: "TagDefinitions",
                    principalColumns: ["TenantId", "TypeId", "FieldCode"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TagValues",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                FieldCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                TypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "BINARY")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagValues", x => new { x.TenantId, x.NodeId, x.FieldCode });
                table.ForeignKey(
                    name: "FK_TagValues_ReportingNodes_TenantId_TypeId_NodeId",
                    columns: x => new { x.TenantId, x.TypeId, x.NodeId },
                    principalTable: "ReportingNodes",
                    principalColumns: ["TenantId", "TypeId", "Id"],
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagValues_TagDefinitions_TenantId_TypeId_FieldCode",
                    columns: x => new { x.TenantId, x.TypeId, x.FieldCode },
                    principalTable: "TagDefinitions",
                    principalColumns: ["TenantId", "TypeId", "FieldCode"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BurstRecords_TenantId_DeviceId",
            table: "BurstRecords",
            columns: ["TenantId", "DeviceId"]);

        migrationBuilder.CreateIndex(
            name: "IX_BurstRecords_TenantId_SignalId_DeviceId_BucketSize_CapturedOn",
            table: "BurstRecords",
            columns: ["TenantId", "SignalId", "DeviceId", "BucketSize", "CapturedOn"]);

        migrationBuilder.CreateIndex(
            name: "IX_LinkAvailability_TenantId_ChildId",
            table: "LinkAvailability",
            columns: ["TenantId", "ChildId"]);

        migrationBuilder.CreateIndex(
            name: "IX_PulseRecords_TenantId_ChannelId_NodeId_Granularity_Day",
            table: "PulseRecords",
            columns: ["TenantId", "ChannelId", "NodeId", "Granularity", "Day"]);

        migrationBuilder.CreateIndex(
            name: "IX_PulseRecords_TenantId_NodeId",
            table: "PulseRecords",
            columns: ["TenantId", "NodeId"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_376A9D14450DCDD5569A28C0",
            table: "ReportingNodes",
            columns: ["TenantId", "NestedSetTreeId", "NestedSetLeft"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_4EEB97220F62CC09CFBCE5A0",
            table: "ReportingNodes",
            columns: ["TenantId", "NestedSetTreeId", "NestedSetParentId", "Name", "Id"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_C3ABE5F497469EAEB475DCE7",
            table: "ReportingNodes",
            columns: ["TenantId", "NestedSetTreeId", "NestedSetParentId", "NestedSetPosition"]);

        migrationBuilder.CreateIndex(
            name: "IX_NestedSet_F78DC0A704C9319A09D7F22B",
            table: "ReportingNodes",
            columns: ["TenantId", "NestedSetTreeId", "NestedSetRight"]);

        migrationBuilder.CreateIndex(
            name: "IX_ReportingNodes_TenantId_NestedSetParentId",
            table: "ReportingNodes",
            columns: ["TenantId", "NestedSetParentId"]);

        migrationBuilder.CreateIndex(
            name: "IX_SignalChannels_TenantId_MeasurementTypeId",
            table: "SignalChannels",
            columns: ["TenantId", "MeasurementTypeId"]);

        migrationBuilder.CreateIndex(
            name: "IX_SweepRecords_TenantId_ReceiverId",
            table: "SweepRecords",
            columns: ["TenantId", "ReceiverId"]);

        migrationBuilder.CreateIndex(
            name: "IX_SweepRecords_TenantId_StreamId_ReceiverId_Resolution_CompletedOn",
            table: "SweepRecords",
            columns: ["TenantId", "StreamId", "ReceiverId", "Resolution", "CompletedOn"]);

        migrationBuilder.CreateIndex(
            name: "IX_TagValues_TenantId_TypeId_FieldCode_Value_NodeId",
            table: "TagValues",
            columns: ["TenantId", "TypeId", "FieldCode", "Value", "NodeId"]);

        migrationBuilder.CreateIndex(
            name: "IX_TagValues_TenantId_TypeId_NodeId",
            table: "TagValues",
            columns: ["TenantId", "TypeId", "NodeId"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BurstRecords");

        migrationBuilder.DropTable(
            name: "DokaNestedSetTrees_F2BE5E0377B809DD");

        migrationBuilder.DropTable(
            name: "LinkAvailability");

        migrationBuilder.DropTable(
            name: "PulseRecords");

        migrationBuilder.DropTable(
            name: "SweepRecords");

        migrationBuilder.DropTable(
            name: "TagChoices");

        migrationBuilder.DropTable(
            name: "TagValues");

        migrationBuilder.DropTable(
            name: "SignalChannels");

        migrationBuilder.DropTable(
            name: "ReportingNodes");

        migrationBuilder.DropTable(
            name: "TagDefinitions");

        migrationBuilder.DropTable(
            name: "NodeKinds");
    }
}
