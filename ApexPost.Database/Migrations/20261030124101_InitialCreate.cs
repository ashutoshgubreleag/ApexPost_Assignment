using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ApexPost.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Action = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    StaffId = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    Role = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    Controller = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    ControllerAction = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    HttpMethod = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RouteValuesJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FieldsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.CheckConstraint("CK_AuditEntries_Outcome", "[Outcome] IN ('Succeeded', 'Rejected', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OpeningHours = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "Active")
                        .Annotation("Relational:DefaultConstraintName", "DF_Branches_Status"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Branches_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_Branches_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.UniqueConstraint("UQ_Branches_BranchCode", x => x.BranchCode);
                    table.CheckConstraint("CK_Branches_BranchCode_Format", "[BranchCode] LIKE 'BR-[0-9][0-9][0-9]'");
                    table.CheckConstraint("CK_Branches_Status", "[Status] IN ('Active', 'Closed')");
                });

            migrationBuilder.CreateTable(
                name: "ProductCategories",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShippingZones",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ZoneCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsInternational = table.Column<bool>(type: "bit", nullable: false),
                    IsRemote = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ShippingZones_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_ShippingZones_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingZones", x => x.Id);
                    table.UniqueConstraint("UQ_ShippingZones_ZoneCode", x => x.ZoneCode);
                    table.CheckConstraint("CK_ShippingZones_ZoneCode_Format", "[ZoneCode] LIKE 'Z[0-9][0-9]'");
                });

            migrationBuilder.CreateTable(
                name: "Counters",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Counters_IsActive"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Counters_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_Counters_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counters", x => x.Id);
                    table.UniqueConstraint("UQ_Counters_Branch_Number", x => new { x.BranchCode, x.Number });
                    table.CheckConstraint("CK_Counters_Number_Positive", "[Number] BETWEEN 1 AND 50");
                    table.ForeignKey(
                        name: "FK_Counters_Branches",
                        column: x => x.BranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    Recipient = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RaisedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Notifications_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_Notifications_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Type", "[Type] IN ('LowStock', 'HighValueMoneyOrder', 'TransactionVoided', 'TransactionRefunded', 'PriceRevision', 'SyncConflict')");
                    table.ForeignKey(
                        name: "FK_Notifications_Branches",
                        column: x => x.BranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sku = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    TaxCategory = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Products_IsActive"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Products_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_Products_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.UniqueConstraint("UQ_Products_Sku", x => x.Sku);
                    table.CheckConstraint("CK_Products_Sku_Format", "[Sku] LIKE '[A-Z][A-Z][A-Z]-[0-9][0-9][0-9]'");
                    table.CheckConstraint("CK_Products_TaxCategory", "[TaxCategory] IN ('Standard', 'Reduced', 'Exempt')");
                    table.CheckConstraint("CK_Products_UnitPrice_Positive", "[UnitPrice] > 0");
                    table.ForeignKey(
                        name: "FK_Products_ProductCategories",
                        column: x => x.CategoryId,
                        principalSchema: "dbo",
                        principalTable: "ProductCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Staff",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    HomeBranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Staff_IsActive"),
                    PasswordHash = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Staff_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_Staff_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staff", x => x.Id);
                    table.CheckConstraint("CK_Staff_Email_Format", "[Email] LIKE '%_@_%'");
                    table.ForeignKey(
                        name: "FK_Staff_Branches",
                        column: x => x.HomeBranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Staff_Roles",
                        column: x => x.RoleId,
                        principalSchema: "dbo",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingRates",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ZoneCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    ShippingType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    BaseRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PerKgRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ShippingRates_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_ShippingRates_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingRates", x => x.Id);
                    table.CheckConstraint("CK_ShippingRates_BaseRate_Positive", "[BaseRate] > 0");
                    table.CheckConstraint("CK_ShippingRates_PerKgRate_NonNegative", "[PerKgRate] >= 0");
                    table.CheckConstraint("CK_ShippingRates_ShippingType", "[ShippingType] IN ('Letter', 'Parcel')");
                    table.ForeignKey(
                        name: "FK_ShippingRates_ShippingZones",
                        column: x => x.ZoneCode,
                        principalSchema: "dbo",
                        principalTable: "ShippingZones",
                        principalColumn: "ZoneCode",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchStock",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Sku = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    QuantityOnHand = table.Column<int>(type: "int", nullable: false),
                    ReorderThreshold = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_BranchStock_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_BranchStock_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchStock", x => x.Id);
                    table.UniqueConstraint("UQ_BranchStock_Branch_Sku", x => new { x.BranchCode, x.Sku });
                    table.CheckConstraint("CK_BranchStock_QuantityOnHand_NonNegative", "[QuantityOnHand] >= 0");
                    table.CheckConstraint("CK_BranchStock_ReorderThreshold_NonNegative", "[ReorderThreshold] >= 0");
                    table.ForeignKey(
                        name: "FK_BranchStock_Branches",
                        column: x => x.BranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BranchStock_Products",
                        column: x => x.Sku,
                        principalSchema: "dbo",
                        principalTable: "Products",
                        principalColumn: "Sku",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CounterTransactions",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CounterNumber = table.Column<int>(type: "int", nullable: false),
                    ClerkStaffId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, defaultValue: "Counter")
                        .Annotation("Relational:DefaultConstraintName", "DF_CounterTransactions_Source"),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "Open")
                        .Annotation("Relational:DefaultConstraintName", "DF_CounterTransactions_Status"),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PassThroughAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VoidReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AuthorisedByStaffId = table.Column<int>(type: "int", nullable: true),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: true, computedColumnSql: "CONVERT(date, [ClosedAtUtc])", stored: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CounterTransactions_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_CounterTransactions_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterTransactions", x => x.Id);
                    table.CheckConstraint("CK_CounterTransactions_Amounts_NonNegative", "[Subtotal] >= 0 AND [TaxAmount] >= 0 AND [PassThroughAmount] >= 0 AND [TotalAmount] >= 0");
                    table.CheckConstraint("CK_CounterTransactions_Closed_HasDate", "[Status] = 'Open' OR [ClosedAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_CounterTransactions_Reversal_HasReason", "[Status] NOT IN ('Voided', 'Refunded') OR ([VoidReason] IS NOT NULL AND [AuthorisedByStaffId] IS NOT NULL)");
                    table.CheckConstraint("CK_CounterTransactions_Source", "[Source] IN ('Counter', 'OfflineTerminal')");
                    table.CheckConstraint("CK_CounterTransactions_Status", "[Status] IN ('Open', 'Paid', 'Voided', 'Refunded')");
                    table.CheckConstraint("CK_CounterTransactions_Total", "[TotalAmount] = [Subtotal] + [TaxAmount] + [PassThroughAmount]");
                    table.ForeignKey(
                        name: "FK_CounterTransactions_Branches",
                        column: x => x.BranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CounterTransactions_Counters",
                        columns: x => new { x.BranchCode, x.CounterNumber },
                        principalSchema: "dbo",
                        principalTable: "Counters",
                        principalColumns: new[] { "BranchCode", "Number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CounterTransactions_Staff_AuthorisedBy",
                        column: x => x.AuthorisedByStaffId,
                        principalSchema: "dbo",
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CounterTransactions_Staff_Clerk",
                        column: x => x.ClerkStaffId,
                        principalSchema: "dbo",
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Sku = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Delta = table.Column<int>(type: "int", nullable: false),
                    QuantityAfter = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ChangedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_StockAdjustments_ChangedAtUtc")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustments", x => x.Id);
                    table.CheckConstraint("CK_StockAdjustments_Delta_NonZero", "[Delta] <> 0");
                    table.CheckConstraint("CK_StockAdjustments_QuantityAfter_NonNegative", "[QuantityAfter] >= 0");
                    table.ForeignKey(
                        name: "FK_StockAdjustments_BranchStock",
                        columns: x => new { x.BranchCode, x.Sku },
                        principalSchema: "dbo",
                        principalTable: "BranchStock",
                        principalColumns: new[] { "BranchCode", "Sku" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentityVerifications",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DocumentTypeCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    DocumentLast4 = table.Column<string>(type: "char(4)", unicode: false, fixedLength: true, maxLength: 4, nullable: false),
                    ResultCode = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_IdentityVerifications_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_IdentityVerifications_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityVerifications", x => x.Id);
                    table.CheckConstraint("CK_IdentityVerifications_DocumentLast4_Length", "LEN([DocumentLast4]) = 4");
                    table.CheckConstraint("CK_IdentityVerifications_DocumentTypeCode", "[DocumentTypeCode] IN ('PP', 'DL', 'NI')");
                    table.CheckConstraint("CK_IdentityVerifications_ResultCode", "[ResultCode] IN ('PASS', 'FAIL')");
                    table.ForeignKey(
                        name: "FK_IdentityVerifications_Branches",
                        column: x => x.BranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdentityVerifications_CounterTransactions",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "CounterTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdentityVerifications_Staff",
                        column: x => x.StaffId,
                        principalSchema: "dbo",
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Payments_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_Payments_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0");
                    table.CheckConstraint("CK_Payments_Method", "[Method] IN ('Cash', 'Card', 'MobileWallet')");
                    table.ForeignKey(
                        name: "FK_Payments_CounterTransactions",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "CounterTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrackingNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ShippingType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ServiceLevel = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    ZoneCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    ChargeableWeightKg = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    RecipientLine1 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RecipientCity = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    RecipientState = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    RecipientPostcode = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    QuotedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Shipments_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_Shipments_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(7,1)", precision: 7, scale: 1, nullable: false),
                    LengthCm = table.Column<decimal>(type: "decimal(7,1)", precision: 7, scale: 1, nullable: false),
                    WidthCm = table.Column<decimal>(type: "decimal(7,1)", precision: 7, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.CheckConstraint("CK_Shipments_ChargeableWeightKg_Positive", "[ChargeableWeightKg] > 0");
                    table.CheckConstraint("CK_Shipments_Dimensions_Positive", "[LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0");
                    table.CheckConstraint("CK_Shipments_Price_Positive", "[Price] > 0");
                    table.CheckConstraint("CK_Shipments_ServiceLevel", "[ServiceLevel] IN ('Standard', 'Express', 'Registered')");
                    table.CheckConstraint("CK_Shipments_ShippingType", "[ShippingType] IN ('Letter', 'Parcel')");
                    table.CheckConstraint("CK_Shipments_Status", "[Status] IN ('Quoted', 'Lodged', 'Cancelled')");
                    table.CheckConstraint("CK_Shipments_WeightKg_Positive", "[WeightKg] > 0");
                    table.ForeignKey(
                        name: "FK_Shipments_Branches",
                        column: x => x.BranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_CounterTransactions",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "CounterTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_ShippingZones",
                        column: x => x.ZoneCode,
                        principalSchema: "dbo",
                        principalTable: "ShippingZones",
                        principalColumn: "ZoneCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncInbox",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    LocalTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ServerTransactionId = table.Column<int>(type: "int", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SyncInbox_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_SyncInbox_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncInbox", x => x.Id);
                    table.CheckConstraint("CK_SyncInbox_Outcome", "[Outcome] IN ('Accepted', 'AcceptedWithWarnings', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_SyncInbox_Branches",
                        column: x => x.BranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncInbox_CounterTransactions",
                        column: x => x.ServerTransactionId,
                        principalSchema: "dbo",
                        principalTable: "CounterTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionLines",
                schema: "dbo",
                columns: table => new
                {
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    LineType = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ItemCode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxCategory = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PassThroughAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TransactionLines_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_TransactionLines_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionLines", x => new { x.TransactionId, x.LineNumber });
                    table.CheckConstraint("CK_TransactionLines_Amounts_NonNegative", "[UnitPrice] >= 0 AND [TaxAmount] >= 0 AND [LineTotal] >= 0 AND [PassThroughAmount] >= 0");
                    table.CheckConstraint("CK_TransactionLines_LineType", "[LineType] IN ('RetailItem', 'Shipping', 'MoneyOrder', 'IdentityVerification')");
                    table.CheckConstraint("CK_TransactionLines_Quantity_Positive", "[Quantity] > 0");
                    table.CheckConstraint("CK_TransactionLines_TaxCategory", "[TaxCategory] IN ('Standard', 'Reduced', 'Exempt')");
                    table.ForeignKey(
                        name: "FK_TransactionLines_CounterTransactions",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "CounterTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MoneyOrders",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SerialNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Sender = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdentityVerificationId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_MoneyOrders_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_MoneyOrders_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyOrders", x => x.Id);
                    table.CheckConstraint("CK_MoneyOrders_Amount_Positive", "[Amount] > 0");
                    table.CheckConstraint("CK_MoneyOrders_Fee_NonNegative", "[Fee] >= 0");
                    table.CheckConstraint("CK_MoneyOrders_Issued_HasDate", "[Status] <> 'Issued' OR [IssuedAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_MoneyOrders_Status", "[Status] IN ('Pending', 'Issued', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_MoneyOrders_Branches",
                        column: x => x.BranchCode,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyOrders_CounterTransactions",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "CounterTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyOrders_IdentityVerifications",
                        column: x => x.IdentityVerificationId,
                        principalSchema: "dbo",
                        principalTable: "IdentityVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncConflicts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SyncInboxEntryId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ItemCode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    RaisedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SyncConflicts_CreatedAtUtc"),
                    CreatedBy = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_SyncConflicts_CreatedBy"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflicts", x => x.Id);
                    table.CheckConstraint("CK_SyncConflicts_Status", "[Status] IN ('Open', 'Resolved')");
                    table.CheckConstraint("CK_SyncConflicts_Type", "[Type] IN ('PriceVariance', 'StockShortfall')");
                    table.ForeignKey(
                        name: "FK_SyncConflicts_SyncInbox",
                        column: x => x.SyncInboxEntryId,
                        principalSchema: "dbo",
                        principalTable: "SyncInbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Branches",
                columns: new[] { "Id", "BranchCode", "CreatedAtUtc", "CreatedBy", "ModifiedAtUtc", "Name", "OpeningHours", "Region" },
                values: new object[,]
                {
                    { 1, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, "Sydney CBD", "Mon-Fri 08:30-17:30; Sat 09:00-12:00", "NSW" },
                    { 2, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, "Parramatta", "Mon-Fri 09:00-17:00", "NSW" },
                    { 3, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, "Melbourne Central", "Mon-Fri 08:30-17:30; Sat 09:00-13:00", "VIC" },
                    { 4, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, "Brisbane Riverside", "Mon-Fri 09:00-17:00", "QLD" },
                    { 5, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, "Perth Hay Street", "Mon-Fri 08:30-17:00", "WA" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Branches",
                columns: new[] { "Id", "BranchCode", "CreatedAtUtc", "CreatedBy", "ModifiedAtUtc", "Name", "OpeningHours", "Region", "Status" },
                values: new object[] { 6, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, "Hobart Elizabeth Street", "Closed for refurbishment", "TAS", "Closed" });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "ProductCategories",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 0, "Packaging" },
                    { 1, "Stationery" },
                    { 2, "Office supplies" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Roles",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 0, "Sells at the counter of one branch.", "CounterClerk" },
                    { 1, "Runs one branch: voids, refunds, stock and staff.", "BranchManager" },
                    { 2, "Read-only access to every branch and the audit log.", "Auditor" },
                    { 3, "Manages branches, staff, prices and maintenance.", "Administrator" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "ShippingZones",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedBy", "IsInternational", "IsRemote", "ModifiedAtUtc", "Name", "ZoneCode" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", false, false, null, "Local metro", "Z01" },
                    { 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", false, false, null, "Intrastate", "Z02" },
                    { 3, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", false, false, null, "Interstate east coast", "Z03" },
                    { 4, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", false, false, null, "Interstate west & NT", "Z04" },
                    { 5, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", false, true, null, "Remote Australia", "Z05" },
                    { 6, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", false, false, null, "Tasmania", "Z06" },
                    { 7, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, false, null, "New Zealand", "Z07" },
                    { 8, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, false, null, "Asia Pacific", "Z08" },
                    { 9, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, false, null, "Europe & UK", "Z09" },
                    { 10, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, false, null, "Americas", "Z10" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Counters",
                columns: new[] { "Id", "BranchCode", "CreatedAtUtc", "CreatedBy", "IsActive", "ModifiedAtUtc", "Name", "Number" },
                values: new object[,]
                {
                    { 1, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 1", 1 },
                    { 2, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 2", 2 },
                    { 3, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 3", 3 },
                    { 4, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 1", 1 },
                    { 5, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 2", 2 },
                    { 6, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 1", 1 },
                    { 7, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 2", 2 },
                    { 8, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 3", 3 },
                    { 9, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 1", 1 },
                    { 10, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 2", 2 },
                    { 11, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 1", 1 },
                    { 12, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 2", 2 },
                    { 13, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 1", 1 },
                    { 14, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Counter 2", 2 }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Products",
                columns: new[] { "Id", "CategoryId", "CreatedAtUtc", "CreatedBy", "IsActive", "ModifiedAtUtc", "Name", "Sku", "TaxCategory", "UnitPrice" },
                values: new object[,]
                {
                    { 1, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Small padded bag", "PKG-001", "Standard", 2.95m },
                    { 2, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Medium padded bag", "PKG-002", "Standard", 3.95m },
                    { 3, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Large padded bag", "PKG-003", "Standard", 4.95m },
                    { 4, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Small box (Size 1)", "PKG-004", "Standard", 2.5m },
                    { 5, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Medium box (Size 3)", "PKG-005", "Standard", 3.75m },
                    { 6, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Large box (Size 5)", "PKG-006", "Standard", 5.25m },
                    { 7, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Tube 1m", "PKG-007", "Standard", 6.5m },
                    { 8, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Bubble wrap roll 3m", "PKG-008", "Standard", 7.95m },
                    { 9, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Packing tape 50m", "PKG-009", "Standard", 4.5m },
                    { 10, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Prepaid satchel 500g", "PKG-010", "Standard", 11.0m },
                    { 11, 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Prepaid satchel 3kg", "PKG-011", "Standard", 16.5m },
                    { 12, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "DL envelopes (pack of 25)", "STN-001", "Standard", 4.25m },
                    { 13, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "C4 envelopes (pack of 10)", "STN-002", "Standard", 5.5m },
                    { 14, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Greeting card - Birthday", "STN-003", "Standard", 5.95m },
                    { 15, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Greeting card - Charity", "STN-004", "Exempt", 4.95m },
                    { 16, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Ballpoint pens (pack of 10)", "STN-005", "Standard", 6.95m },
                    { 17, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "A5 notebook", "STN-006", "Reduced", 3.95m },
                    { 18, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Exercise book 96pg", "STN-007", "Reduced", 1.95m },
                    { 19, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Postcard set (6)", "STN-008", "Standard", 7.5m },
                    { 20, 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Address labels (100)", "STN-009", "Standard", 8.95m },
                    { 21, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "A4 copy paper 500 sheets", "OFF-001", "Standard", 8.5m },
                    { 22, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Stapler", "OFF-002", "Standard", 12.95m },
                    { 23, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Staples (5000)", "OFF-003", "Standard", 3.5m },
                    { 24, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Lever arch file", "OFF-004", "Standard", 4.95m },
                    { 25, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Highlighters (4)", "OFF-005", "Standard", 5.95m },
                    { 26, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Sticky notes (12 pads)", "OFF-006", "Standard", 9.95m },
                    { 27, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "USB flash drive 64GB", "OFF-007", "Standard", 19.95m },
                    { 28, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Printer ink black", "OFF-008", "Standard", 34.95m },
                    { 29, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Calculator", "OFF-009", "Standard", 14.95m },
                    { 30, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Scissors", "OFF-010", "Standard", 6.5m },
                    { 31, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Desk organiser", "OFF-011", "Standard", 17.95m },
                    { 32, 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, "Document wallet (5)", "OFF-012", "Reduced", 7.95m }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "ShippingRates",
                columns: new[] { "Id", "BaseRate", "CreatedAtUtc", "CreatedBy", "ModifiedAtUtc", "PerKgRate", "ShippingType", "ZoneCode" },
                values: new object[,]
                {
                    { 1, 1.2m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z01" },
                    { 2, 8.95m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 1.1m, "Parcel", "Z01" },
                    { 3, 1.2m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z02" },
                    { 4, 10.45m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 1.5m, "Parcel", "Z02" },
                    { 5, 1.2m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z03" },
                    { 6, 12.95m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 2.1m, "Parcel", "Z03" },
                    { 7, 1.2m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z04" },
                    { 8, 14.95m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 2.6m, "Parcel", "Z04" },
                    { 9, 1.4m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z05" },
                    { 10, 16.95m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 3.2m, "Parcel", "Z05" },
                    { 11, 1.2m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z06" },
                    { 12, 13.45m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 2.3m, "Parcel", "Z06" },
                    { 13, 3.1m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z07" },
                    { 14, 21.5m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 4.8m, "Parcel", "Z07" },
                    { 15, 3.6m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z08" },
                    { 16, 27.5m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 6.9m, "Parcel", "Z08" },
                    { 17, 4.2m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z09" },
                    { 18, 34.95m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 9.4m, "Parcel", "Z09" },
                    { 19, 4.2m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 0m, "Letter", "Z10" },
                    { 20, 36.95m, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 9.9m, "Parcel", "Z10" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Staff",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedBy", "Email", "HomeBranchCode", "IsActive", "ModifiedAtUtc", "Name", "PasswordHash", "RoleId" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "admin@apexpost.local", "BR-001", true, null, "Avery Admin", "PBKDF2$100000$1WIw9+8h6K5o76wd72maGw==$KZny1K7LyeKvOadi3GBxl0XCdJSG0+KkJNDCjKyHro4=", 3 },
                    { 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "it.admin@apexpost.local", "BR-003", true, null, "Jordan Systems", "PBKDF2$100000$CXBMqGxXRNAbgchZk2zHZA==$Pu38WljeYYPqfjBhfo92GXS0GJN3CXFHBtIJ44XlGRQ=", 3 },
                    { 3, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "auditor@apexpost.local", "BR-001", true, null, "Riley Auditor", "PBKDF2$100000$ozNg6QPAFAV4fsrFcQPiSg==$TJ1ktc3GcCRfq5Psg11Tnla3SeNwYgMNqE9Cm+OV1LY=", 2 },
                    { 4, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "compliance.auditor@apexpost.local", "BR-003", true, null, "Morgan Compliance", "PBKDF2$100000$d+XqtiIt9TlxJBTbae2zTQ==$QKardjd4NnZ2iGo1YBZ3GL5BX1JNpQXAlaTU6k1ZmaY=", 2 },
                    { 5, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "review.auditor@apexpost.local", "BR-004", true, null, "Casey Review", "PBKDF2$100000$epU5eevKmG2dU2RqAzvHxQ==$NYlnfDmg5uQ2GDnMaWSXlwo3X8M1D8lpHAaQMp/UEOU=", 2 },
                    { 6, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "manager.br001@apexpost.local", "BR-001", true, null, "Priya Sharma", "PBKDF2$100000$3vxPIs+dx4ZGdrhR67Dp0Q==$HeG/06HGcYGEf/KpePFjhXJrmLq+NOQbe1BEUAp/dAY=", 1 },
                    { 7, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br001a@apexpost.local", "BR-001", true, null, "Liam Rossi", "PBKDF2$100000$cZcsbbOw/SHSGlheKiweQg==$4EuwPX4//ofgPO+7FwZq6Szo9J4Xqqr9TfY+BDNH+Ms=", 0 },
                    { 8, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br001b@apexpost.local", "BR-001", true, null, "Mei Wilson", "PBKDF2$100000$Pa2tPnCIRfxEVW29X6mTBg==$AC7ZakHUeRJipE0dCfEiLtphxVm46zNmBasC22I42iw=", 0 },
                    { 9, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "manager.br002@apexpost.local", "BR-002", true, null, "Noah Smith", "PBKDF2$100000$io6/PT+dG376H257kN7MUA==$2z8BPp0jv03GHMvl9KV2SVPSgCWYxsMP1ev7Sk1eJmk=", 1 },
                    { 10, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br002a@apexpost.local", "BR-002", true, null, "Aisha Silva", "PBKDF2$100000$cD9wDOjXHw57LSzEf+PAGw==$zlZ0Wp00QrdcRfD6Pt2t3gle0rmSMJcdEDZMaW3m/dc=", 0 },
                    { 11, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br002b@apexpost.local", "BR-002", true, null, "Oliver Ali", "PBKDF2$100000$1l9wtwekEF8lWfToSppTrQ==$KcoxxsP15vXXxPIvsY30Bx2hEbBs1Grvqzrqoab2uko=", 0 },
                    { 12, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "manager.br003@apexpost.local", "BR-003", true, null, "Sofia Rossi", "PBKDF2$100000$VQWcRTjUmTuBX2p3pvqeMw==$nCipRjuZCnaoKzcXRkmVu7lSnftoGcy99S2NrMdYBHo=", 1 },
                    { 13, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br003a@apexpost.local", "BR-003", true, null, "Ethan Taylor", "PBKDF2$100000$KXZdY4w2z9SNpXAiDSeU6w==$I80J8YXng5Fxa2Bdl91XVRaOR7C4rktNiCalaxPgoT0=", 0 },
                    { 14, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br003b@apexpost.local", "BR-003", true, null, "Hana Patel", "PBKDF2$100000$B0eOJuXjCRrcIxrUtza8aA==$GBLj36BqhCyz/gBjCZ1Q8BE4+DgARdFav7esjwEVeWw=", 0 },
                    { 15, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "manager.br004@apexpost.local", "BR-004", true, null, "Lucas Silva", "PBKDF2$100000$ycIgbQwm7VmK/QIGZLi+Bw==$w2CsxVJi8dgustx8SKtf3M2hOEroXX+kP2zQU/bCAjw=", 1 },
                    { 16, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br004a@apexpost.local", "BR-004", true, null, "Zara Nguyen", "PBKDF2$100000$M8izjA6a9fcOgfbEisxNPg==$vB8kTR4KXY//av7qoGyhAMf/xAjbSd26f6CJF+IqIfs=", 0 },
                    { 17, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br004b@apexpost.local", "BR-004", true, null, "Mateo Chen", "PBKDF2$100000$fbri+al+23VUJ8V5j5BmIw==$B1f4j9zq5RRVG5ktbLg169buPsRBG0JHavS4DwP6v0s=", 0 },
                    { 18, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "manager.br005@apexpost.local", "BR-005", true, null, "Chloe Taylor", "PBKDF2$100000$1avwfuhVLWOgx0Wl545kQw==$ptwqOh+nvuIUmF6P2CQNk7rilxIuK7M6nVOOLVsMzCA=", 1 },
                    { 19, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br005a@apexpost.local", "BR-005", true, null, "Ravi Khan", "PBKDF2$100000$egVU22oPCEzeXzgH4FzAXA==$5DGkbvCpl0QW/wzyRRvdvwSQPONWe7kp7NQmChV7shQ=", 0 },
                    { 20, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br005b@apexpost.local", "BR-005", true, null, "Priya Brown", "PBKDF2$100000$3XlNIs5grpASr07Qq7CNZg==$mt1g0M0YZnMQ6giGUD+7ahonJ9VYO81WFs823VZomGc=", 0 },
                    { 21, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "manager.br006@apexpost.local", "BR-006", true, null, "Liam Nguyen", "PBKDF2$100000$4RNgPAv76P+swHrSV+1wNw==$xArh8MoP88ATzuaXyFqG9lysA/2ZJ/yGupjuUpRqs3M=", 1 },
                    { 22, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br006a@apexpost.local", "BR-006", true, null, "Mei Wilson", "PBKDF2$100000$O5dMYZslJ3kzQfz637oWZg==$98M7mBe7dWQM/0R5Ew61M27HZCgTfs9p1vM5iLOVz+U=", 0 },
                    { 23, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "clerk.br006b@apexpost.local", "BR-006", true, null, "Noah Sato", "PBKDF2$100000$08NH1yl4GUORdSyHHkI+mQ==$xkRZv8AT1/bbWErWLkED0q+MPrURzM1O2QAd/inZXxQ=", 0 }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Staff",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedBy", "Email", "HomeBranchCode", "ModifiedAtUtc", "Name", "PasswordHash", "RoleId" },
                values: new object[] { 24, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "former.clerk@apexpost.local", "BR-002", null, "Sam Former", "PBKDF2$100000$iO8a8/lUHDryGTU7lIB2Eg==$Ln0nIbxJlcYfZehAWufRRDJ2pggz9xRbzhyUsaGLH5Y=", 0 });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "BranchStock",
                columns: new[] { "Id", "BranchCode", "CreatedAtUtc", "CreatedBy", "ModifiedAtUtc", "QuantityOnHand", "ReorderThreshold", "Sku", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 44, 5, "PKG-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 66, 12, "PKG-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 66, 5, "PKG-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 58, 10, "PKG-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 78, 10, "PKG-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 6, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 21, 10, "PKG-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 7, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 78, 10, "PKG-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 8, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 78, 8, "PKG-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 9, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 67, 12, "PKG-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 10, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 42, 10, "PKG-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 11, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 70, 12, "PKG-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 12, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 58, 5, "STN-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 13, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 33, 10, "STN-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 14, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 76, 10, "STN-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 15, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 58, 12, "STN-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 16, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 34, 5, "STN-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 17, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 43, 8, "STN-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 18, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 22, 8, "STN-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 19, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 29, 12, "STN-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 20, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 79, 5, "STN-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 21, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 23, 8, "OFF-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 22, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 65, 8, "OFF-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 23, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 50, 10, "OFF-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 24, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 60, 5, "OFF-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 25, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 52, 10, "OFF-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 26, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 25, 12, "OFF-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 27, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 16, 10, "OFF-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 28, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 25, 10, "OFF-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 29, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 16, 5, "OFF-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 30, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 29, 5, "OFF-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 31, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 38, 10, "OFF-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 32, "BR-001", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 1, 12, "OFF-012", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 33, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 51, 12, "PKG-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 34, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 70, 12, "PKG-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 35, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 53, 10, "PKG-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 36, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 69, 12, "PKG-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 37, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 25, 12, "PKG-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 38, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 57, 10, "PKG-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 39, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 19, 5, "PKG-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 40, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 60, 10, "PKG-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 41, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 53, 10, "PKG-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 42, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 53, 10, "PKG-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 43, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 65, 10, "PKG-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 44, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 73, 12, "STN-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 45, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 69, 10, "STN-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 46, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 33, 10, "STN-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 47, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 73, 10, "STN-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 48, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 64, 8, "STN-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 49, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 33, 12, "STN-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 50, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 47, 12, "STN-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 51, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 61, 12, "STN-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 52, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 43, 12, "STN-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 53, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 60, 10, "OFF-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 54, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 58, 8, "OFF-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 55, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 52, 12, "OFF-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 56, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 26, 5, "OFF-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 57, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 29, 10, "OFF-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 58, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 32, 12, "OFF-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 59, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 3, 5, "OFF-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 60, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 58, 8, "OFF-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 61, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 42, 5, "OFF-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 62, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 8, 10, "OFF-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 63, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 46, 5, "OFF-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 64, "BR-002", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 7, 8, "OFF-012", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 65, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 24, 8, "PKG-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 66, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 69, 5, "PKG-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 67, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 76, 10, "PKG-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 68, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 57, 5, "PKG-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 69, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 77, 8, "PKG-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 70, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 63, 12, "PKG-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 71, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 25, 12, "PKG-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 72, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 15, 8, "PKG-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 73, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 18, 5, "PKG-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 74, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 4, 5, "PKG-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 75, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 33, 5, "PKG-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 76, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 46, 12, "STN-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 77, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 34, 5, "STN-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 78, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 49, 10, "STN-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 79, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 72, 5, "STN-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 80, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 45, 10, "STN-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 81, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 28, 10, "STN-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 82, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 57, 12, "STN-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 83, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 64, 8, "STN-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 84, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 39, 12, "STN-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 85, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 39, 5, "OFF-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 86, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 39, 8, "OFF-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 87, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 69, 10, "OFF-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 88, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 50, 10, "OFF-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 89, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 79, 10, "OFF-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 90, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 19, 5, "OFF-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 91, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 17, 12, "OFF-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 92, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 67, 12, "OFF-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 93, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 37, 12, "OFF-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 94, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 63, 8, "OFF-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 95, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 2, 5, "OFF-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 96, "BR-003", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 1, 12, "OFF-012", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 97, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 71, 10, "PKG-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 98, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 36, 10, "PKG-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 99, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 28, 5, "PKG-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 100, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 70, 5, "PKG-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 101, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 6, 10, "PKG-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 102, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 40, 12, "PKG-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 103, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 51, 5, "PKG-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 104, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 22, 10, "PKG-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 105, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 35, 12, "PKG-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 106, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 34, 8, "PKG-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 107, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 31, 12, "PKG-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 108, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 56, 10, "STN-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 109, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 22, 5, "STN-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 110, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 37, 5, "STN-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 111, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 44, 12, "STN-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 112, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 29, 5, "STN-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 113, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 59, 5, "STN-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 114, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 78, 8, "STN-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 115, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 78, 5, "STN-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 116, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 71, 10, "STN-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 117, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 50, 8, "OFF-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 118, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 27, 8, "OFF-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 119, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 69, 12, "OFF-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 120, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 20, 5, "OFF-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 121, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 54, 5, "OFF-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 122, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 55, 8, "OFF-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 123, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 48, 8, "OFF-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 124, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 68, 5, "OFF-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 125, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 80, 12, "OFF-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 126, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 31, 8, "OFF-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 127, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 42, 12, "OFF-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 128, "BR-004", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 20, 10, "OFF-012", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 129, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 75, 5, "PKG-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 130, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 15, 12, "PKG-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 131, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 70, 10, "PKG-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 132, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 4, 5, "PKG-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 133, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 77, 10, "PKG-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 134, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 5, 12, "PKG-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 135, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 53, 10, "PKG-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 136, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 44, 5, "PKG-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 137, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 26, 10, "PKG-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 138, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 28, 12, "PKG-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 139, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 25, 8, "PKG-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 140, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 59, 10, "STN-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 141, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 64, 5, "STN-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 142, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 16, 12, "STN-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 143, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 20, 5, "STN-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 144, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 73, 8, "STN-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 145, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 72, 12, "STN-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 146, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 55, 5, "STN-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 147, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 2, 5, "STN-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 148, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 74, 8, "STN-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 149, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 48, 8, "OFF-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 150, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 21, 8, "OFF-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 151, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 4, 10, "OFF-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 152, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 34, 10, "OFF-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 153, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 25, 10, "OFF-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 154, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 63, 10, "OFF-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 155, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 76, 8, "OFF-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 156, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 56, 8, "OFF-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 157, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 48, 5, "OFF-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 158, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 79, 10, "OFF-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 159, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 5, 8, "OFF-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 160, "BR-005", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 70, 10, "OFF-012", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 161, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 6, 10, "PKG-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 162, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 39, 10, "PKG-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 163, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 2, 12, "PKG-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 164, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 23, 5, "PKG-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 165, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 17, 12, "PKG-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 166, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 66, 8, "PKG-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 167, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 9, 12, "PKG-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 168, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 33, 8, "PKG-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 169, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 71, 12, "PKG-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 170, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 30, 5, "PKG-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 171, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 80, 10, "PKG-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 172, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 36, 5, "STN-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 173, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 66, 10, "STN-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 174, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 73, 12, "STN-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 175, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 32, 5, "STN-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 176, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 34, 8, "STN-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 177, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 38, 12, "STN-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 178, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 25, 12, "STN-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 179, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 50, 10, "STN-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 180, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 67, 5, "STN-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 181, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 77, 8, "OFF-001", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 182, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 62, 5, "OFF-002", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 183, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 34, 10, "OFF-003", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 184, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 67, 8, "OFF-004", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 185, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 15, 12, "OFF-005", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 186, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 71, 10, "OFF-006", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 187, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 4, 10, "OFF-007", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 188, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 28, 10, "OFF-008", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 189, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 75, 5, "OFF-009", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 190, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 68, 5, "OFF-010", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 191, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 33, 10, "OFF-011", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) },
                    { 192, "BR-006", new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, 45, 12, "OFF-012", new DateTime(2026, 10, 25, 7, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "CIX_AuditEntries_TimestampUtc",
                schema: "dbo",
                table: "AuditEntries",
                column: "TimestampUtc")
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Branch_Timestamp",
                schema: "dbo",
                table: "AuditEntries",
                columns: new[] { "BranchCode", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchStock_Sku",
                schema: "dbo",
                table: "BranchStock",
                column: "Sku");

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_AuthorisedByStaffId",
                schema: "dbo",
                table: "CounterTransactions",
                column: "AuthorisedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_Branch_Counter",
                schema: "dbo",
                table: "CounterTransactions",
                columns: new[] { "BranchCode", "CounterNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_ClerkStaffId",
                schema: "dbo",
                table: "CounterTransactions",
                column: "ClerkStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerifications_Branch_VerifiedAt",
                schema: "dbo",
                table: "IdentityVerifications",
                columns: new[] { "BranchCode", "VerifiedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerifications_StaffId",
                schema: "dbo",
                table: "IdentityVerifications",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerifications_TransactionId",
                schema: "dbo",
                table: "IdentityVerifications",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "UQ_IdentityVerifications_Reference",
                schema: "dbo",
                table: "IdentityVerifications",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoneyOrders_BranchCode",
                schema: "dbo",
                table: "MoneyOrders",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyOrders_IdentityVerificationId",
                schema: "dbo",
                table: "MoneyOrders",
                column: "IdentityVerificationId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyOrders_TransactionId",
                schema: "dbo",
                table: "MoneyOrders",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "UQ_MoneyOrders_SerialNumber",
                schema: "dbo",
                table: "MoneyOrders",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Branch_RaisedAt",
                schema: "dbo",
                table: "Notifications",
                columns: new[] { "BranchCode", "RaisedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TransactionId",
                schema: "dbo",
                table: "Payments",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "UQ_ProductCategories_Name",
                schema: "dbo",
                table: "ProductCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                schema: "dbo",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "UQ_Roles_Name",
                schema: "dbo",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_BranchCode",
                schema: "dbo",
                table: "Shipments",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TransactionId",
                schema: "dbo",
                table: "Shipments",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ZoneCode",
                schema: "dbo",
                table: "Shipments",
                column: "ZoneCode");

            migrationBuilder.CreateIndex(
                name: "UQ_Shipments_TrackingNumber",
                schema: "dbo",
                table: "Shipments",
                column: "TrackingNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ShippingRates_Zone_Type",
                schema: "dbo",
                table: "ShippingRates",
                columns: new[] { "ZoneCode", "ShippingType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staff_HomeBranchCode",
                schema: "dbo",
                table: "Staff",
                column: "HomeBranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_RoleId",
                schema: "dbo",
                table: "Staff",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UQ_Staff_Email",
                schema: "dbo",
                table: "Staff",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_Branch_Sku_ChangedAt",
                schema: "dbo",
                table: "StockAdjustments",
                columns: new[] { "BranchCode", "Sku", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_Open",
                schema: "dbo",
                table: "SyncConflicts",
                column: "Status",
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_SyncInboxEntryId",
                schema: "dbo",
                table: "SyncConflicts",
                column: "SyncInboxEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncInbox_BranchCode",
                schema: "dbo",
                table: "SyncInbox",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_SyncInbox_ServerTransactionId",
                schema: "dbo",
                table: "SyncInbox",
                column: "ServerTransactionId");

            migrationBuilder.CreateIndex(
                name: "UQ_SyncInbox_Device_LocalTransaction",
                schema: "dbo",
                table: "SyncInbox",
                columns: new[] { "DeviceId", "LocalTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_LineType_ItemCode",
                schema: "dbo",
                table: "TransactionLines",
                columns: new[] { "LineType", "ItemCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "MoneyOrders",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Shipments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ShippingRates",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StockAdjustments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SyncConflicts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TransactionLines",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "IdentityVerifications",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ShippingZones",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BranchStock",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SyncInbox",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CounterTransactions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ProductCategories",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Counters",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Staff",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Branches",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "dbo");
        }
    }
}
