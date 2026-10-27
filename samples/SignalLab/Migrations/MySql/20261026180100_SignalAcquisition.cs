#nullable disable

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.Migrations.MySql;

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
                Scope = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false),
                TreeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false),
                Lifecycle = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DokaNestedSetTrees_F2BE5E0377B809DD", x => new { x.Scope, x.TreeId });
            });

        migrationBuilder.CreateTable(
            name: "NodeKinds",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Id = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NodeKinds", x => new { x.TenantId, x.Id });
            });

        migrationBuilder.CreateTable(
            name: "ReportingNodes",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Id = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                TypeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                IsVisible = table.Column<bool>(type: "tinyint(1)", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                NestedSetDepth = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                NestedSetLeft = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                NestedSetParentId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: true)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                NestedSetPosition = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                NestedSetRight = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                NestedSetTreeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReportingNodes", x => new { x.TenantId, x.Id });
                table.UniqueConstraint("AK_ReportingNodes_TenantId_TypeId_Id", x => new { x.TenantId, x.TypeId, x.Id });
                table.CheckConstraint("CK_NestedSet_DepthMin_706EF85AC4FF", "`NestedSetDepth` >= 0");
                table.CheckConstraint("CK_NestedSet_LeftMin_9C29CC100F5D", "`NestedSetLeft` >= 1");
                table.CheckConstraint("CK_NestedSet_PositionMin_5C730A1DDB5A", "`NestedSetPosition` >= 0");
                table.CheckConstraint("CK_NestedSet_RightAfterLeft_D817C86A9A56", "`NestedSetRight` > `NestedSetLeft`");
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
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Id = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                MeasurementTypeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                UnitCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
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
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                TypeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Label = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
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
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ParentId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ChildId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FromDate = table.Column<DateOnly>(type: "date", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ToDate = table.Column<DateOnly>(type: "date", nullable: true)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LinkAvailability", x => new { x.TenantId, x.ParentId, x.ChildId, x.FromDate });
                table.CheckConstraint("CK_LinkAvailability_OrderedDays", "`ToDate` IS NULL OR `ToDate` >= `FromDate`");
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
                Sequence = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.AutoIncrement),
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                SignalId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                DeviceId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                BucketSize = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                CapturedOn = table.Column<DateOnly>(type: "date", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Magnitude = table.Column<double>(type: "double", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
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
                Sequence = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.AutoIncrement),
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ChannelId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                NodeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Granularity = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Day = table.Column<DateOnly>(type: "date", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                PulseCount = table.Column<double>(type: "double", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
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
                Sequence = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.AutoIncrement),
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                StreamId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                ReceiverId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Resolution = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                CompletedOn = table.Column<DateOnly>(type: "date", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                IntegratedTicks = table.Column<double>(type: "double", nullable: false)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
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
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                TypeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Value = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
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
                TenantId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                NodeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                FieldCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                TypeId = table.Column<Guid>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                    .Annotation("Doka:MySql:GuidFormat", MySqlGuidFormat.Binary16)
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None),
                Value = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("Doka:MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.None)
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
            name: "IX_SweepRecords_TenantId_StreamId_ReceiverId_Resolution_Complet~",
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
