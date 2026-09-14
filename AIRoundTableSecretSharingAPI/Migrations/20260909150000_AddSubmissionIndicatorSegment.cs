using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    [Migration("20260909150000_AddSubmissionIndicatorSegment")]
    public partial class AddSubmissionIndicatorSegment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_ProducerId_Country_Month_EpochId",
                table: "Submissions");

            migrationBuilder.AlterColumn<string>(
                name: "Month",
                table: "Submissions",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "Indicator",
                table: "Submissions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Segment",
                table: "Submissions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProducerId_Country_Month_Indicator_Segment_EpochId",
                table: "Submissions",
                columns: new[] { "ProducerId", "Country", "Month", "Indicator", "Segment", "EpochId" },
                unique: true,
                filter: "[ProducerId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_ProducerId_Country_Month_Indicator_Segment_EpochId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "Indicator",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "Segment",
                table: "Submissions");

            migrationBuilder.AlterColumn<string>(
                name: "Month",
                table: "Submissions",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(7)",
                oldMaxLength: 7);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProducerId_Country_Month_EpochId",
                table: "Submissions",
                columns: new[] { "ProducerId", "Country", "Month", "EpochId" },
                unique: true,
                filter: "[ProducerId] IS NOT NULL");
        }
    }
}
