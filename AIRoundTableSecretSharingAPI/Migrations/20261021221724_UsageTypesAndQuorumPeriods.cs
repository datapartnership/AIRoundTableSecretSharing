using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    public partial class UsageTypesAndQuorumPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuorumResponses");

            migrationBuilder.DropColumn(
                name: "QuorumComplete",
                table: "Epochs");

            migrationBuilder.AddColumn<int>(
                name: "QuorumPeriodId",
                table: "Epochs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageType",
                table: "Epochs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "api");

            migrationBuilder.CreateTable(
                name: "QuorumAnswers",
                columns: table => new
                {
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    ProducerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Indicator = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MaskedCount = table.Column<long>(type: "bigint", nullable: false),
                    MaskedTagA = table.Column<long>(type: "bigint", nullable: false),
                    MaskedTagB = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuorumAnswers", x => new { x.PeriodId, x.ProducerId, x.Country, x.Indicator });
                });

            migrationBuilder.CreateTable(
                name: "QuorumPeriods",
                columns: table => new
                {
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    UsageType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProducerIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuorumComplete = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuorumPeriods", x => x.PeriodId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuorumAnswers");

            migrationBuilder.DropTable(
                name: "QuorumPeriods");

            migrationBuilder.DropColumn(
                name: "QuorumPeriodId",
                table: "Epochs");

            migrationBuilder.DropColumn(
                name: "UsageType",
                table: "Epochs");

            migrationBuilder.AddColumn<bool>(
                name: "QuorumComplete",
                table: "Epochs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "QuorumResponses",
                columns: table => new
                {
                    EpochId = table.Column<int>(type: "int", nullable: false),
                    ProducerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Month = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Indicator = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Segment = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MaskedCount = table.Column<long>(type: "bigint", nullable: false),
                    MaskedTagA = table.Column<long>(type: "bigint", nullable: false),
                    MaskedTagB = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuorumResponses", x => new { x.EpochId, x.ProducerId, x.Country, x.Month, x.Indicator, x.Segment });
                });
        }
    }
}
