using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddQuorumCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "QuorumComplete",
                table: "Epochs",
                type: "bit",
                nullable: false,
                defaultValue: true); // pre-existing epochs skip the Quorum Check

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
                    Participates = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuorumResponses", x => new { x.EpochId, x.ProducerId, x.Country, x.Month, x.Indicator, x.Segment });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuorumResponses");

            migrationBuilder.DropColumn(
                name: "QuorumComplete",
                table: "Epochs");
        }
    }
}
