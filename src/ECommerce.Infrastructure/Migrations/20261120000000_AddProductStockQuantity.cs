using System;
using ECommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the authoritative sellable quantity used by basket stock validation.
    /// Existing products start with zero stock until inventory is supplied.
    /// </summary>
    [DbContext(typeof(StoreDbContext))]
    [Migration("20261120000000_AddProductStockQuantity")]
    public partial class AddProductStockQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "Products");
        }
    }
}
