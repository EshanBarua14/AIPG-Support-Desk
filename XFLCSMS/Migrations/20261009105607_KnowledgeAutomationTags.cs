using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XFLCSMS.Migrations
{
    public partial class KnowledgeAutomationTags : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EngineerId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReminderAt",
                table: "Issues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderCount",
                table: "Issues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusSince",
                table: "Issues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KbArticles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    Views = table.Column<int>(type: "int", nullable: false),
                    Helpful = table.Column<int>(type: "int", nullable: false),
                    NotHelpful = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceIssueId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KbArticles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KbArticles_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IssueId = table.Column<int>(type: "int", nullable: false),
                    OtherIssueId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketLinks_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "IssueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketWatchers",
                columns: table => new
                {
                    IssueId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Since = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketWatchers", x => new { x.IssueId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TicketWatchers_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "IssueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketTags",
                columns: table => new
                {
                    IssueId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketTags", x => new { x.IssueId, x.TagId });
                    table.ForeignKey(
                        name: "FK_TicketTags_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "IssueId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KbArticles_IsPublished",
                table: "KbArticles",
                column: "IsPublished");

            migrationBuilder.CreateIndex(
                name: "IX_KbArticles_ProductId",
                table: "KbArticles",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketLinks_IssueId",
                table: "TicketLinks",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLinks_OtherIssueId",
                table: "TicketLinks",
                column: "OtherIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketTags_TagId",
                table: "TicketTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketWatchers_UserId",
                table: "TicketWatchers",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KbArticles");

            migrationBuilder.DropTable(
                name: "TicketLinks");

            migrationBuilder.DropTable(
                name: "TicketTags");

            migrationBuilder.DropTable(
                name: "TicketWatchers");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropColumn(
                name: "EngineerId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LastReminderAt",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ReminderCount",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "StatusSince",
                table: "Issues");
        }
    }
}
