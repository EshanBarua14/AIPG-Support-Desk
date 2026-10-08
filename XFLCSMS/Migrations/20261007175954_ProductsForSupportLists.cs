using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XFLCSMS.Migrations
{
    public partial class ProductsForSupportLists : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "SupportTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "SupportSubCatagories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "SupportCatagories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "Issues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "AffectedSectionss",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTypes_ProductId",
                table: "SupportTypes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportSubCatagories_ProductId",
                table: "SupportSubCatagories",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportCatagories_ProductId",
                table: "SupportCatagories",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ProductId",
                table: "Issues",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AffectedSectionss_ProductId",
                table: "AffectedSectionss",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AffectedSectionss_Products_ProductId",
                table: "AffectedSectionss",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Issues_Products_ProductId",
                table: "Issues",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportCatagories_Products_ProductId",
                table: "SupportCatagories",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportSubCatagories_Products_ProductId",
                table: "SupportSubCatagories",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTypes_Products_ProductId",
                table: "SupportTypes",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AffectedSectionss_Products_ProductId",
                table: "AffectedSectionss");

            migrationBuilder.DropForeignKey(
                name: "FK_Issues_Products_ProductId",
                table: "Issues");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportCatagories_Products_ProductId",
                table: "SupportCatagories");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportSubCatagories_Products_ProductId",
                table: "SupportSubCatagories");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTypes_Products_ProductId",
                table: "SupportTypes");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropIndex(
                name: "IX_SupportTypes_ProductId",
                table: "SupportTypes");

            migrationBuilder.DropIndex(
                name: "IX_SupportSubCatagories_ProductId",
                table: "SupportSubCatagories");

            migrationBuilder.DropIndex(
                name: "IX_SupportCatagories_ProductId",
                table: "SupportCatagories");

            migrationBuilder.DropIndex(
                name: "IX_Issues_ProductId",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_AffectedSectionss_ProductId",
                table: "AffectedSectionss");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "SupportTypes");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "SupportSubCatagories");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "SupportCatagories");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "AffectedSectionss");
        }
    }
}
