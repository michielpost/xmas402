using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace xmas402.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Gifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    From = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    To = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Transaction = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Network = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Asset = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Value = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NextValue = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GiftType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CreatedDateTime = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gifts", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Gifts");
        }
    }
}
