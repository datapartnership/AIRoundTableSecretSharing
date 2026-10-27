using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    public partial class QuarterlyEpochs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing epochs report 4 months; only epochs created from now on are quarters
            migrationBuilder.AddColumn<int>(
                name: "MonthCount",
                table: "Epochs",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AlterColumn<int>(
                name: "MonthCount",
                table: "Epochs",
                type: "int",
                nullable: false,
                defaultValue: 3,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonthCount",
                table: "Epochs");
        }
    }
}
