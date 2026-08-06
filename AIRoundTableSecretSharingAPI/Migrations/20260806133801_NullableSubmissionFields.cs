using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    public partial class NullableSubmissionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_ProducerId_Country_Month_EpochId",
                table: "Submissions");

            migrationBuilder.AlterColumn<string>(
                name: "Signature",
                table: "Submissions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "ProducerId",
                table: "Submissions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProducerId_Country_Month_EpochId",
                table: "Submissions",
                columns: new[] { "ProducerId", "Country", "Month", "EpochId" },
                unique: true,
                filter: "[ProducerId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_ProducerId_Country_Month_EpochId",
                table: "Submissions");

            migrationBuilder.AlterColumn<string>(
                name: "Signature",
                table: "Submissions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProducerId",
                table: "Submissions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProducerId_Country_Month_EpochId",
                table: "Submissions",
                columns: new[] { "ProducerId", "Country", "Month", "EpochId" },
                unique: true);
        }
    }
}
