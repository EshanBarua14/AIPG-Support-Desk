using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XFLCSMS.Migrations
{
    public partial class SupportDesk : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstResponseAt",
                table: "Issues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingSince",
                table: "Issues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RatedAt",
                table: "Issues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "Issues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingComment",
                table: "Issues",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReopenCount",
                table: "Issues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolveDueAt",
                table: "Issues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "Issues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseDueAt",
                table: "Issues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlaNotices",
                table: "Issues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SlaPausedMinutes",
                table: "Issues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "MessageId",
                table: "Attachments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CannedReplies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CannedReplies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IssueId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    AuthorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AuthorRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FromStaff = table.Column<bool>(type: "bit", nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketMessages_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "IssueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ResolveDueAt",
                table: "Issues",
                column: "ResolveDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_MessageId",
                table: "Attachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_IssueId_Id",
                table: "TicketMessages",
                columns: new[] { "IssueId", "Id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CannedReplies");

            migrationBuilder.DropTable(
                name: "TicketMessages");

            migrationBuilder.DropIndex(
                name: "IX_Issues_ResolveDueAt",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_MessageId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "FirstResponseAt",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "PendingSince",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "RatedAt",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "RatingComment",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ReopenCount",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ResolveDueAt",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ResponseDueAt",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "SlaNotices",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "SlaPausedMinutes",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "Attachments");
        }
    }
}
