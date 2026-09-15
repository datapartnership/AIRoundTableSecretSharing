using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCryptoByEpochAndDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PublicKeys",
                table: "PublicKeys");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Ciphertexts",
                table: "Ciphertexts");

            migrationBuilder.AddColumn<int>(
                name: "EpochId",
                table: "PublicKeys",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "PublicKeys",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EpochId",
                table: "Ciphertexts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SenderDeviceId",
                table: "Ciphertexts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecipientDeviceId",
                table: "Ciphertexts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE PublicKeys
                SET EpochId = COALESCE(
                        (SELECT TOP 1 EpochId FROM Epochs WHERE EndDate IS NULL ORDER BY StartDate DESC),
                        0),
                    DeviceId = 'legacy'
                WHERE DeviceId = '';

                UPDATE Ciphertexts
                SET EpochId = COALESCE(
                        (SELECT TOP 1 EpochId FROM Epochs WHERE EndDate IS NULL ORDER BY StartDate DESC),
                        0),
                    SenderDeviceId = 'legacy',
                    RecipientDeviceId = 'legacy'
                WHERE SenderDeviceId = '' AND RecipientDeviceId = '';
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PublicKeys",
                table: "PublicKeys",
                columns: new[] { "EpochId", "ProducerId", "DeviceId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Ciphertexts",
                table: "Ciphertexts",
                columns: new[] { "EpochId", "SenderId", "SenderDeviceId", "RecipientId", "RecipientDeviceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PublicKeys",
                table: "PublicKeys");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Ciphertexts",
                table: "Ciphertexts");

            migrationBuilder.DropColumn(
                name: "EpochId",
                table: "PublicKeys");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "PublicKeys");

            migrationBuilder.DropColumn(
                name: "EpochId",
                table: "Ciphertexts");

            migrationBuilder.DropColumn(
                name: "SenderDeviceId",
                table: "Ciphertexts");

            migrationBuilder.DropColumn(
                name: "RecipientDeviceId",
                table: "Ciphertexts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PublicKeys",
                table: "PublicKeys",
                column: "ProducerId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Ciphertexts",
                table: "Ciphertexts",
                columns: new[] { "SenderId", "RecipientId" });
        }
    }
}
