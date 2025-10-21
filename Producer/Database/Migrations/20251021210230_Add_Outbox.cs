using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Producer.Database.Migrations
{
    /// <inheritdoc />
    public partial class Add_Outbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    occurred_on_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_on_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_content_is_json", "ISJSON([content]) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_on_utc_processed_on_utc",
                table: "outbox_messages",
                columns: new[] { "occurred_on_utc", "processed_on_utc" },
                filter: "[processed_on_utc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
