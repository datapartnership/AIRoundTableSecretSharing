using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    public partial class MaskQuorumAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Earlier unmasked answers cannot be converted to masked ones
            migrationBuilder.Sql("DELETE FROM QuorumResponses");

            migrationBuilder.DropColumn(
                name: "Participates",
                table: "QuorumResponses");

            migrationBuilder.AddColumn<long>(
                name: "MaskedCount",
                table: "QuorumResponses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MaskedTagA",
                table: "QuorumResponses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MaskedTagB",
                table: "QuorumResponses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaskedCount",
                table: "QuorumResponses");

            migrationBuilder.DropColumn(
                name: "MaskedTagA",
                table: "QuorumResponses");

            migrationBuilder.DropColumn(
                name: "MaskedTagB",
                table: "QuorumResponses");

            migrationBuilder.AddColumn<bool>(
                name: "Participates",
                table: "QuorumResponses",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
