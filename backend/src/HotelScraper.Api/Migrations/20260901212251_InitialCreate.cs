using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelScraper.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hotels",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    booking_id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    address = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    stars = table.Column<int>(type: "INTEGER", nullable: true),
                    review_score = table.Column<double>(type: "REAL", nullable: true),
                    image_url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    distance_km = table.Column<double>(type: "REAL", nullable: true),
                    active = table.Column<bool>(type: "INTEGER", nullable: false),
                    city = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hotels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prices",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    hotel_id = table.Column<int>(type: "INTEGER", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    price_eur = table.Column<double>(type: "REAL", nullable: false),
                    room_type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hotels_booking_id",
                table: "hotels",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_hotels_city",
                table: "hotels",
                column: "city");

            migrationBuilder.CreateIndex(
                name: "uq_booking_city",
                table: "hotels",
                columns: new[] { "booking_id", "city" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prices_date",
                table: "prices",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_prices_hotel_id",
                table: "prices",
                column: "hotel_id");

            migrationBuilder.CreateIndex(
                name: "uq_hotel_date_room",
                table: "prices",
                columns: new[] { "hotel_id", "date", "room_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hotels");

            migrationBuilder.DropTable(
                name: "prices");

            migrationBuilder.DropTable(
                name: "settings");
        }
    }
}
