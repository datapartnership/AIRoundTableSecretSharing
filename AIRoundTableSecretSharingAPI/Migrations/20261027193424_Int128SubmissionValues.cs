using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    public partial class Int128SubmissionValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "Submissions",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            // Existing epochs were masked mod 2^64; only epochs created from now on use 128 bits
            migrationBuilder.AddColumn<int>(
                name: "MaskBits",
                table: "Epochs",
                type: "int",
                nullable: false,
                defaultValue: 64);

            migrationBuilder.AlterColumn<int>(
                name: "MaskBits",
                table: "Epochs",
                type: "int",
                nullable: false,
                defaultValue: 128,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 64);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaskBits",
                table: "Epochs");

            migrationBuilder.AlterColumn<long>(
                name: "Value",
                table: "Submissions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldUnicode: false,
                oldMaxLength: 40);
        }
    }
}
