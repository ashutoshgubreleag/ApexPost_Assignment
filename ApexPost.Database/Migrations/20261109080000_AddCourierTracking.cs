using System;
using ApexPost.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Database.Migrations
{
    /// <summary>
    /// Week 4 Part B. Adds the courier tracking tables (TrackedShipments, ShipmentScans) next to the
    /// Week 3 tables without touching them. ShipmentScans.ScanId is UNIQUE: it is the idempotency key
    /// for bulk scan uploads. Hand-written in the same shape as the generated migrations; if
    /// `dotnet ef migrations has-pending-model-changes` reports a difference, delete this file and the
    /// snapshot edits and run `dotnet ef migrations add AddCourierTracking` instead.
    /// </summary>
    [DbContext(typeof(ApexPostDbContext))]
    [Migration("20261109080000_AddCourierTracking")]
    public partial class AddCourierTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedShipments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrackingNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientPhone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TrackedShipments_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_TrackedShipments_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedShipments", x => x.Id);
                    table.CheckConstraint("CK_TrackedShipments_Status", "[Status] IN ('Created', 'PickedUp', 'InTransit', 'OutForDelivery', 'Delivered', 'Failed')");
                    table.CheckConstraint("CK_TrackedShipments_TrackingNumber_Format", "[TrackingNumber] LIKE 'APX-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'");
                    table.CheckConstraint("CK_TrackedShipments_WeightKg_Range", "[WeightKg] >= 0.1 AND [WeightKg] <= 70");
                });

            migrationBuilder.CreateTable(
                name: "ShipmentScans",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    ScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RecipientSignatureName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ShipmentScans_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_ShipmentScans_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentScans", x => x.Id);
                    table.CheckConstraint("CK_ShipmentScans_Type", "[Type] IN ('PickedUp', 'Arrived', 'Departed', 'OutForDelivery', 'Delivered', 'DeliveryFailed')");
                    table.ForeignKey(
                        name: "FK_ShipmentScans_TrackedShipments",
                        column: x => x.ShipmentId,
                        principalSchema: "dbo",
                        principalTable: "TrackedShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_TrackedShipments_TrackingNumber",
                schema: "dbo",
                table: "TrackedShipments",
                column: "TrackingNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedShipments_Status_City",
                schema: "dbo",
                table: "TrackedShipments",
                columns: new[] { "Status", "City" });

            migrationBuilder.CreateIndex(
                name: "UQ_ShipmentScans_ScanId",
                schema: "dbo",
                table: "ShipmentScans",
                column: "ScanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentScans_Shipment_ScannedAt",
                schema: "dbo",
                table: "ShipmentScans",
                columns: new[] { "ShipmentId", "ScannedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentScans",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TrackedShipments",
                schema: "dbo");
        }
    }
}
