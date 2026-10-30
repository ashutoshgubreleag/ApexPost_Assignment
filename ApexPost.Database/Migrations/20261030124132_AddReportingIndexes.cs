using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Zone_Level_QuotedAt",
                schema: "dbo",
                table: "Shipments",
                columns: new[] { "ZoneCode", "ServiceLevel", "QuotedAtUtc" },
                descending: new[] { false, false, true })
                .Annotation("SqlServer:Include", new[] { "Status", "WeightKg", "Price", "BranchCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyOrders_Branch_RequestedAt_Audited",
                schema: "dbo",
                table: "MoneyOrders",
                columns: new[] { "BranchCode", "RequestedAtUtc" },
                filter: "[Status] IN ('Issued', 'Cancelled')")
                .Annotation("SqlServer:Include", new[] { "Amount", "Fee", "Status", "SerialNumber", "IdentityVerificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_Branch_BusinessDate",
                schema: "dbo",
                table: "CounterTransactions",
                columns: new[] { "BranchCode", "BusinessDate" })
                .Annotation("SqlServer:Include", new[] { "Status", "Subtotal", "TaxAmount", "PassThroughAmount", "TotalAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_ClosedAt_Paid",
                schema: "dbo",
                table: "CounterTransactions",
                column: "ClosedAtUtc",
                filter: "[Status] = 'Paid'")
                .Annotation("SqlServer:Include", new[] { "BranchCode", "TotalAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchStock_Branch_Covering",
                schema: "dbo",
                table: "BranchStock",
                column: "BranchCode")
                .Annotation("SqlServer:Include", new[] { "Sku", "QuantityOnHand", "ReorderThreshold" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shipments_Zone_Level_QuotedAt",
                schema: "dbo",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_MoneyOrders_Branch_RequestedAt_Audited",
                schema: "dbo",
                table: "MoneyOrders");

            migrationBuilder.DropIndex(
                name: "IX_CounterTransactions_Branch_BusinessDate",
                schema: "dbo",
                table: "CounterTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CounterTransactions_ClosedAt_Paid",
                schema: "dbo",
                table: "CounterTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BranchStock_Branch_Covering",
                schema: "dbo",
                table: "BranchStock");
        }
    }
}
