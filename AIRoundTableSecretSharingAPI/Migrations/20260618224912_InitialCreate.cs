using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIRoundTableSecretSharingAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Epochs",
                columns: table => new
                {
                    EpochId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProducerIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProducerCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Epochs", x => x.EpochId);
                });

            migrationBuilder.CreateTable(
                name: "Producers",
                columns: table => new
                {
                    ProducerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JoinedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Producers", x => x.ProducerId);
                });

            migrationBuilder.CreateTable(
                name: "PublicKeys",
                columns: table => new
                {
                    ProducerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PublicKeyBase64 = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicKeys", x => x.ProducerId);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProducerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Month = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    EpochId = table.Column<int>(type: "int", nullable: false),
                    Signature = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProducerId_Country_Month_EpochId",
                table: "Submissions",
                columns: new[] { "ProducerId", "Country", "Month", "EpochId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Epochs");

            migrationBuilder.DropTable(
                name: "Producers");

            migrationBuilder.DropTable(
                name: "PublicKeys");

            migrationBuilder.DropTable(
                name: "Submissions");
        }
    }
}
