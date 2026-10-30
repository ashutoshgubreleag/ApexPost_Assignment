IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[AuditEntries] (
        [Id] uniqueidentifier NOT NULL,
        [TimestampUtc] datetime2(3) NOT NULL,
        [Action] varchar(60) NOT NULL,
        [StaffId] varchar(10) NULL,
        [Role] varchar(30) NULL,
        [BranchCode] varchar(10) NULL,
        [Controller] varchar(60) NOT NULL,
        [ControllerAction] varchar(60) NOT NULL,
        [HttpMethod] varchar(10) NOT NULL,
        [Path] nvarchar(400) NOT NULL,
        [RouteValuesJson] nvarchar(2000) NOT NULL,
        [FieldsJson] nvarchar(4000) NOT NULL,
        [StatusCode] int NOT NULL,
        [Outcome] varchar(10) NOT NULL,
        [Error] nvarchar(1000) NULL,
        [DurationMs] bigint NOT NULL,
        [CorrelationId] varchar(64) NOT NULL,
        CONSTRAINT [PK_AuditEntries] PRIMARY KEY NONCLUSTERED ([Id]),
        CONSTRAINT [CK_AuditEntries_Outcome] CHECK ([Outcome] IN ('Succeeded', 'Rejected', 'Failed'))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Branches] (
        [Id] int NOT NULL IDENTITY,
        [BranchCode] varchar(10) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Region] nvarchar(60) NOT NULL,
        [OpeningHours] nvarchar(120) NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_Branches_Status] DEFAULT 'Active',
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Branches_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_Branches_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Branches] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_Branches_BranchCode] UNIQUE ([BranchCode]),
        CONSTRAINT [CK_Branches_BranchCode_Format] CHECK ([BranchCode] LIKE 'BR-[0-9][0-9][0-9]'),
        CONSTRAINT [CK_Branches_Status] CHECK ([Status] IN ('Active', 'Closed'))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[ProductCategories] (
        [Id] int NOT NULL,
        [Name] nvarchar(50) NOT NULL,
        CONSTRAINT [PK_ProductCategories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Roles] (
        [Id] int NOT NULL,
        [Name] varchar(30) NOT NULL,
        [Description] nvarchar(200) NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[ShippingZones] (
        [Id] int NOT NULL IDENTITY,
        [ZoneCode] varchar(5) NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [IsInternational] bit NOT NULL,
        [IsRemote] bit NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_ShippingZones_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_ShippingZones_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_ShippingZones] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_ShippingZones_ZoneCode] UNIQUE ([ZoneCode]),
        CONSTRAINT [CK_ShippingZones_ZoneCode_Format] CHECK ([ZoneCode] LIKE 'Z[0-9][0-9]')
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Counters] (
        [Id] int NOT NULL IDENTITY,
        [BranchCode] varchar(10) NOT NULL,
        [Number] int NOT NULL,
        [Name] nvarchar(50) NOT NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_Counters_IsActive] DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Counters_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_Counters_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Counters] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_Counters_Branch_Number] UNIQUE ([BranchCode], [Number]),
        CONSTRAINT [CK_Counters_Number_Positive] CHECK ([Number] BETWEEN 1 AND 50),
        CONSTRAINT [FK_Counters_Branches] FOREIGN KEY ([BranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Notifications] (
        [Id] int NOT NULL IDENTITY,
        [Type] varchar(24) NOT NULL,
        [BranchCode] varchar(10) NULL,
        [Recipient] varchar(254) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [CorrelationId] varchar(64) NOT NULL,
        [RaisedAtUtc] datetime2(3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Notifications_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_Notifications_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Notifications_Type] CHECK ([Type] IN ('LowStock', 'HighValueMoneyOrder', 'TransactionVoided', 'TransactionRefunded', 'PriceRevision', 'SyncConflict')),
        CONSTRAINT [FK_Notifications_Branches] FOREIGN KEY ([BranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Products] (
        [Id] int NOT NULL IDENTITY,
        [Sku] varchar(20) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [CategoryId] int NOT NULL,
        [TaxCategory] varchar(10) NOT NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_Products_IsActive] DEFAULT CAST(1 AS bit),
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Products_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_Products_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_Products_Sku] UNIQUE ([Sku]),
        CONSTRAINT [CK_Products_Sku_Format] CHECK ([Sku] LIKE '[A-Z][A-Z][A-Z]-[0-9][0-9][0-9]'),
        CONSTRAINT [CK_Products_TaxCategory] CHECK ([TaxCategory] IN ('Standard', 'Reduced', 'Exempt')),
        CONSTRAINT [CK_Products_UnitPrice_Positive] CHECK ([UnitPrice] > 0),
        CONSTRAINT [FK_Products_ProductCategories] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[ProductCategories] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Staff] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Email] varchar(254) NOT NULL,
        [RoleId] int NOT NULL,
        [HomeBranchCode] varchar(10) NOT NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_Staff_IsActive] DEFAULT CAST(1 AS bit),
        [PasswordHash] varchar(200) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Staff_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_Staff_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Staff] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Staff_Email_Format] CHECK ([Email] LIKE '%_@_%'),
        CONSTRAINT [FK_Staff_Branches] FOREIGN KEY ([HomeBranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Staff_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[ShippingRates] (
        [Id] int NOT NULL IDENTITY,
        [ZoneCode] varchar(5) NOT NULL,
        [ShippingType] varchar(10) NOT NULL,
        [BaseRate] decimal(18,2) NOT NULL,
        [PerKgRate] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_ShippingRates_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_ShippingRates_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_ShippingRates] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ShippingRates_BaseRate_Positive] CHECK ([BaseRate] > 0),
        CONSTRAINT [CK_ShippingRates_PerKgRate_NonNegative] CHECK ([PerKgRate] >= 0),
        CONSTRAINT [CK_ShippingRates_ShippingType] CHECK ([ShippingType] IN ('Letter', 'Parcel')),
        CONSTRAINT [FK_ShippingRates_ShippingZones] FOREIGN KEY ([ZoneCode]) REFERENCES [dbo].[ShippingZones] ([ZoneCode]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[BranchStock] (
        [Id] int NOT NULL IDENTITY,
        [BranchCode] varchar(10) NOT NULL,
        [Sku] varchar(20) NOT NULL,
        [QuantityOnHand] int NOT NULL,
        [ReorderThreshold] int NOT NULL,
        [UpdatedAtUtc] datetime2(3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_BranchStock_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_BranchStock_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_BranchStock] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_BranchStock_Branch_Sku] UNIQUE ([BranchCode], [Sku]),
        CONSTRAINT [CK_BranchStock_QuantityOnHand_NonNegative] CHECK ([QuantityOnHand] >= 0),
        CONSTRAINT [CK_BranchStock_ReorderThreshold_NonNegative] CHECK ([ReorderThreshold] >= 0),
        CONSTRAINT [FK_BranchStock_Branches] FOREIGN KEY ([BranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BranchStock_Products] FOREIGN KEY ([Sku]) REFERENCES [dbo].[Products] ([Sku]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[CounterTransactions] (
        [Id] int NOT NULL IDENTITY,
        [BranchCode] varchar(10) NOT NULL,
        [CounterNumber] int NOT NULL,
        [ClerkStaffId] int NOT NULL,
        [Source] varchar(16) NOT NULL CONSTRAINT [DF_CounterTransactions_Source] DEFAULT 'Counter',
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_CounterTransactions_Status] DEFAULT 'Open',
        [Subtotal] decimal(18,2) NOT NULL,
        [TaxAmount] decimal(18,2) NOT NULL,
        [PassThroughAmount] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [VoidReason] nvarchar(200) NULL,
        [AuthorisedByStaffId] int NULL,
        [OpenedAtUtc] datetime2(3) NOT NULL,
        [ClosedAtUtc] datetime2(3) NULL,
        [BusinessDate] AS CONVERT(date, [ClosedAtUtc]) PERSISTED,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_CounterTransactions_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_CounterTransactions_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CounterTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CounterTransactions_Amounts_NonNegative] CHECK ([Subtotal] >= 0 AND [TaxAmount] >= 0 AND [PassThroughAmount] >= 0 AND [TotalAmount] >= 0),
        CONSTRAINT [CK_CounterTransactions_Closed_HasDate] CHECK ([Status] = 'Open' OR [ClosedAtUtc] IS NOT NULL),
        CONSTRAINT [CK_CounterTransactions_Reversal_HasReason] CHECK ([Status] NOT IN ('Voided', 'Refunded') OR ([VoidReason] IS NOT NULL AND [AuthorisedByStaffId] IS NOT NULL)),
        CONSTRAINT [CK_CounterTransactions_Source] CHECK ([Source] IN ('Counter', 'OfflineTerminal')),
        CONSTRAINT [CK_CounterTransactions_Status] CHECK ([Status] IN ('Open', 'Paid', 'Voided', 'Refunded')),
        CONSTRAINT [CK_CounterTransactions_Total] CHECK ([TotalAmount] = [Subtotal] + [TaxAmount] + [PassThroughAmount]),
        CONSTRAINT [FK_CounterTransactions_Branches] FOREIGN KEY ([BranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CounterTransactions_Counters] FOREIGN KEY ([BranchCode], [CounterNumber]) REFERENCES [dbo].[Counters] ([BranchCode], [Number]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CounterTransactions_Staff_AuthorisedBy] FOREIGN KEY ([AuthorisedByStaffId]) REFERENCES [dbo].[Staff] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CounterTransactions_Staff_Clerk] FOREIGN KEY ([ClerkStaffId]) REFERENCES [dbo].[Staff] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[StockAdjustments] (
        [Id] int NOT NULL IDENTITY,
        [BranchCode] varchar(10) NOT NULL,
        [Sku] varchar(20) NOT NULL,
        [Delta] int NOT NULL,
        [QuantityAfter] int NOT NULL,
        [Reason] nvarchar(200) NOT NULL,
        [ChangedBy] varchar(50) NOT NULL,
        [ChangedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_StockAdjustments_ChangedAtUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_StockAdjustments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_StockAdjustments_Delta_NonZero] CHECK ([Delta] <> 0),
        CONSTRAINT [CK_StockAdjustments_QuantityAfter_NonNegative] CHECK ([QuantityAfter] >= 0),
        CONSTRAINT [FK_StockAdjustments_BranchStock] FOREIGN KEY ([BranchCode], [Sku]) REFERENCES [dbo].[BranchStock] ([BranchCode], [Sku]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[IdentityVerifications] (
        [Id] int NOT NULL IDENTITY,
        [Reference] varchar(20) NOT NULL,
        [TransactionId] int NOT NULL,
        [BranchCode] varchar(10) NOT NULL,
        [StaffId] int NOT NULL,
        [CustomerName] nvarchar(80) NOT NULL,
        [DocumentTypeCode] char(2) NOT NULL,
        [DocumentLast4] char(4) NOT NULL,
        [ResultCode] varchar(4) NOT NULL,
        [VerifiedAtUtc] datetime2(3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_IdentityVerifications_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_IdentityVerifications_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_IdentityVerifications] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_IdentityVerifications_DocumentLast4_Length] CHECK (LEN([DocumentLast4]) = 4),
        CONSTRAINT [CK_IdentityVerifications_DocumentTypeCode] CHECK ([DocumentTypeCode] IN ('PP', 'DL', 'NI')),
        CONSTRAINT [CK_IdentityVerifications_ResultCode] CHECK ([ResultCode] IN ('PASS', 'FAIL')),
        CONSTRAINT [FK_IdentityVerifications_Branches] FOREIGN KEY ([BranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_IdentityVerifications_CounterTransactions] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[CounterTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_IdentityVerifications_Staff] FOREIGN KEY ([StaffId]) REFERENCES [dbo].[Staff] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Payments] (
        [Id] int NOT NULL IDENTITY,
        [TransactionId] int NOT NULL,
        [Method] varchar(12) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [PaidAtUtc] datetime2(3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Payments_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_Payments_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Payments_Amount_Positive] CHECK ([Amount] > 0),
        CONSTRAINT [CK_Payments_Method] CHECK ([Method] IN ('Cash', 'Card', 'MobileWallet')),
        CONSTRAINT [FK_Payments_CounterTransactions] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[CounterTransactions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Shipments] (
        [Id] int NOT NULL IDENTITY,
        [TrackingNumber] varchar(20) NOT NULL,
        [TransactionId] int NOT NULL,
        [BranchCode] varchar(10) NOT NULL,
        [ShippingType] varchar(10) NOT NULL,
        [ServiceLevel] varchar(12) NOT NULL,
        [ZoneCode] varchar(5) NOT NULL,
        [WeightKg] decimal(9,3) NOT NULL,
        [ChargeableWeightKg] decimal(9,3) NOT NULL,
        [RecipientName] nvarchar(80) NULL,
        [RecipientLine1] nvarchar(120) NULL,
        [RecipientCity] nvarchar(60) NULL,
        [RecipientState] varchar(20) NULL,
        [RecipientPostcode] varchar(12) NULL,
        [Price] decimal(18,2) NOT NULL,
        [Status] varchar(10) NOT NULL,
        [QuotedAtUtc] datetime2(3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Shipments_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_Shipments_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        [HeightCm] decimal(7,1) NOT NULL,
        [LengthCm] decimal(7,1) NOT NULL,
        [WidthCm] decimal(7,1) NOT NULL,
        CONSTRAINT [PK_Shipments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Shipments_ChargeableWeightKg_Positive] CHECK ([ChargeableWeightKg] > 0),
        CONSTRAINT [CK_Shipments_Dimensions_Positive] CHECK ([LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0),
        CONSTRAINT [CK_Shipments_Price_Positive] CHECK ([Price] > 0),
        CONSTRAINT [CK_Shipments_ServiceLevel] CHECK ([ServiceLevel] IN ('Standard', 'Express', 'Registered')),
        CONSTRAINT [CK_Shipments_ShippingType] CHECK ([ShippingType] IN ('Letter', 'Parcel')),
        CONSTRAINT [CK_Shipments_Status] CHECK ([Status] IN ('Quoted', 'Lodged', 'Cancelled')),
        CONSTRAINT [CK_Shipments_WeightKg_Positive] CHECK ([WeightKg] > 0),
        CONSTRAINT [FK_Shipments_Branches] FOREIGN KEY ([BranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Shipments_CounterTransactions] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[CounterTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Shipments_ShippingZones] FOREIGN KEY ([ZoneCode]) REFERENCES [dbo].[ShippingZones] ([ZoneCode]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[SyncInbox] (
        [Id] int NOT NULL IDENTITY,
        [DeviceId] varchar(50) NOT NULL,
        [LocalTransactionId] uniqueidentifier NOT NULL,
        [BranchCode] varchar(10) NOT NULL,
        [ReceivedAtUtc] datetime2(3) NOT NULL,
        [Outcome] varchar(24) NOT NULL,
        [ServerTransactionId] int NULL,
        [Message] nvarchar(400) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_SyncInbox_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_SyncInbox_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_SyncInbox] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SyncInbox_Outcome] CHECK ([Outcome] IN ('Accepted', 'AcceptedWithWarnings', 'Rejected')),
        CONSTRAINT [FK_SyncInbox_Branches] FOREIGN KEY ([BranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SyncInbox_CounterTransactions] FOREIGN KEY ([ServerTransactionId]) REFERENCES [dbo].[CounterTransactions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[TransactionLines] (
        [TransactionId] int NOT NULL,
        [LineNumber] int NOT NULL,
        [LineType] varchar(24) NOT NULL,
        [ItemCode] varchar(30) NOT NULL,
        [Description] nvarchar(200) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [TaxCategory] varchar(10) NOT NULL,
        [TaxAmount] decimal(18,2) NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        [PassThroughAmount] decimal(18,2) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_TransactionLines_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_TransactionLines_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_TransactionLines] PRIMARY KEY ([TransactionId], [LineNumber]),
        CONSTRAINT [CK_TransactionLines_Amounts_NonNegative] CHECK ([UnitPrice] >= 0 AND [TaxAmount] >= 0 AND [LineTotal] >= 0 AND [PassThroughAmount] >= 0),
        CONSTRAINT [CK_TransactionLines_LineType] CHECK ([LineType] IN ('RetailItem', 'Shipping', 'MoneyOrder', 'IdentityVerification')),
        CONSTRAINT [CK_TransactionLines_Quantity_Positive] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_TransactionLines_TaxCategory] CHECK ([TaxCategory] IN ('Standard', 'Reduced', 'Exempt')),
        CONSTRAINT [FK_TransactionLines_CounterTransactions] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[CounterTransactions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[MoneyOrders] (
        [Id] int NOT NULL IDENTITY,
        [SerialNumber] varchar(20) NOT NULL,
        [TransactionId] int NOT NULL,
        [BranchCode] varchar(10) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Fee] decimal(18,2) NOT NULL,
        [Sender] nvarchar(80) NOT NULL,
        [Recipient] nvarchar(80) NOT NULL,
        [IdentityVerificationId] int NULL,
        [Status] varchar(10) NOT NULL,
        [RequestedAtUtc] datetime2(3) NOT NULL,
        [IssuedAtUtc] datetime2(3) NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_MoneyOrders_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_MoneyOrders_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_MoneyOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_MoneyOrders_Amount_Positive] CHECK ([Amount] > 0),
        CONSTRAINT [CK_MoneyOrders_Fee_NonNegative] CHECK ([Fee] >= 0),
        CONSTRAINT [CK_MoneyOrders_Issued_HasDate] CHECK ([Status] <> 'Issued' OR [IssuedAtUtc] IS NOT NULL),
        CONSTRAINT [CK_MoneyOrders_Status] CHECK ([Status] IN ('Pending', 'Issued', 'Cancelled')),
        CONSTRAINT [FK_MoneyOrders_Branches] FOREIGN KEY ([BranchCode]) REFERENCES [dbo].[Branches] ([BranchCode]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MoneyOrders_CounterTransactions] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[CounterTransactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MoneyOrders_IdentityVerifications] FOREIGN KEY ([IdentityVerificationId]) REFERENCES [dbo].[IdentityVerifications] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[SyncConflicts] (
        [Id] int NOT NULL IDENTITY,
        [SyncInboxEntryId] int NOT NULL,
        [Type] varchar(16) NOT NULL,
        [ItemCode] varchar(30) NOT NULL,
        [Details] nvarchar(400) NOT NULL,
        [Status] varchar(10) NOT NULL,
        [RaisedAtUtc] datetime2(3) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_SyncConflicts_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] varchar(50) NOT NULL CONSTRAINT [DF_SyncConflicts_CreatedBy] DEFAULT 'system',
        [ModifiedAtUtc] datetime2(3) NULL,
        CONSTRAINT [PK_SyncConflicts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SyncConflicts_Status] CHECK ([Status] IN ('Open', 'Resolved')),
        CONSTRAINT [CK_SyncConflicts_Type] CHECK ([Type] IN ('PriceVariance', 'StockShortfall')),
        CONSTRAINT [FK_SyncConflicts_SyncInbox] FOREIGN KEY ([SyncInboxEntryId]) REFERENCES [dbo].[SyncInbox] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'Name', N'OpeningHours', N'Region') AND [object_id] = OBJECT_ID(N'[dbo].[Branches]'))
        SET IDENTITY_INSERT [dbo].[Branches] ON;
    EXEC(N'INSERT INTO [dbo].[Branches] ([Id], [BranchCode], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [Name], [OpeningHours], [Region])
    VALUES (1, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, N''Sydney CBD'', N''Mon-Fri 08:30-17:30; Sat 09:00-12:00'', N''NSW''),
    (2, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, N''Parramatta'', N''Mon-Fri 09:00-17:00'', N''NSW''),
    (3, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, N''Melbourne Central'', N''Mon-Fri 08:30-17:30; Sat 09:00-13:00'', N''VIC''),
    (4, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, N''Brisbane Riverside'', N''Mon-Fri 09:00-17:00'', N''QLD''),
    (5, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, N''Perth Hay Street'', N''Mon-Fri 08:30-17:00'', N''WA'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'Name', N'OpeningHours', N'Region') AND [object_id] = OBJECT_ID(N'[dbo].[Branches]'))
        SET IDENTITY_INSERT [dbo].[Branches] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'Name', N'OpeningHours', N'Region', N'Status') AND [object_id] = OBJECT_ID(N'[dbo].[Branches]'))
        SET IDENTITY_INSERT [dbo].[Branches] ON;
    EXEC(N'INSERT INTO [dbo].[Branches] ([Id], [BranchCode], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [Name], [OpeningHours], [Region], [Status])
    VALUES (6, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, N''Hobart Elizabeth Street'', N''Closed for refurbishment'', N''TAS'', ''Closed'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'Name', N'OpeningHours', N'Region', N'Status') AND [object_id] = OBJECT_ID(N'[dbo].[Branches]'))
        SET IDENTITY_INSERT [dbo].[Branches] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[dbo].[ProductCategories]'))
        SET IDENTITY_INSERT [dbo].[ProductCategories] ON;
    EXEC(N'INSERT INTO [dbo].[ProductCategories] ([Id], [Name])
    VALUES (0, N''Packaging''),
    (1, N''Stationery''),
    (2, N''Office supplies'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[dbo].[ProductCategories]'))
        SET IDENTITY_INSERT [dbo].[ProductCategories] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'Name') AND [object_id] = OBJECT_ID(N'[dbo].[Roles]'))
        SET IDENTITY_INSERT [dbo].[Roles] ON;
    EXEC(N'INSERT INTO [dbo].[Roles] ([Id], [Description], [Name])
    VALUES (0, N''Sells at the counter of one branch.'', ''CounterClerk''),
    (1, N''Runs one branch: voids, refunds, stock and staff.'', ''BranchManager''),
    (2, N''Read-only access to every branch and the audit log.'', ''Auditor''),
    (3, N''Manages branches, staff, prices and maintenance.'', ''Administrator'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'Name') AND [object_id] = OBJECT_ID(N'[dbo].[Roles]'))
        SET IDENTITY_INSERT [dbo].[Roles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAtUtc', N'CreatedBy', N'IsInternational', N'IsRemote', N'ModifiedAtUtc', N'Name', N'ZoneCode') AND [object_id] = OBJECT_ID(N'[dbo].[ShippingZones]'))
        SET IDENTITY_INSERT [dbo].[ShippingZones] ON;
    EXEC(N'INSERT INTO [dbo].[ShippingZones] ([Id], [CreatedAtUtc], [CreatedBy], [IsInternational], [IsRemote], [ModifiedAtUtc], [Name], [ZoneCode])
    VALUES (1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(0 AS bit), CAST(0 AS bit), NULL, N''Local metro'', ''Z01''),
    (2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(0 AS bit), CAST(0 AS bit), NULL, N''Intrastate'', ''Z02''),
    (3, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(0 AS bit), CAST(0 AS bit), NULL, N''Interstate east coast'', ''Z03''),
    (4, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(0 AS bit), CAST(0 AS bit), NULL, N''Interstate west & NT'', ''Z04''),
    (5, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(0 AS bit), CAST(1 AS bit), NULL, N''Remote Australia'', ''Z05''),
    (6, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(0 AS bit), CAST(0 AS bit), NULL, N''Tasmania'', ''Z06''),
    (7, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), CAST(0 AS bit), NULL, N''New Zealand'', ''Z07''),
    (8, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), CAST(0 AS bit), NULL, N''Asia Pacific'', ''Z08''),
    (9, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), CAST(0 AS bit), NULL, N''Europe & UK'', ''Z09''),
    (10, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), CAST(0 AS bit), NULL, N''Americas'', ''Z10'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAtUtc', N'CreatedBy', N'IsInternational', N'IsRemote', N'ModifiedAtUtc', N'Name', N'ZoneCode') AND [object_id] = OBJECT_ID(N'[dbo].[ShippingZones]'))
        SET IDENTITY_INSERT [dbo].[ShippingZones] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedAtUtc', N'CreatedBy', N'IsActive', N'ModifiedAtUtc', N'Name', N'Number') AND [object_id] = OBJECT_ID(N'[dbo].[Counters]'))
        SET IDENTITY_INSERT [dbo].[Counters] ON;
    EXEC(N'INSERT INTO [dbo].[Counters] ([Id], [BranchCode], [CreatedAtUtc], [CreatedBy], [IsActive], [ModifiedAtUtc], [Name], [Number])
    VALUES (1, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 1'', 1),
    (2, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 2'', 2),
    (3, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 3'', 3),
    (4, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 1'', 1),
    (5, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 2'', 2),
    (6, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 1'', 1),
    (7, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 2'', 2),
    (8, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 3'', 3),
    (9, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 1'', 1),
    (10, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 2'', 2),
    (11, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 1'', 1),
    (12, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 2'', 2),
    (13, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 1'', 1),
    (14, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Counter 2'', 2)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedAtUtc', N'CreatedBy', N'IsActive', N'ModifiedAtUtc', N'Name', N'Number') AND [object_id] = OBJECT_ID(N'[dbo].[Counters]'))
        SET IDENTITY_INSERT [dbo].[Counters] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CategoryId', N'CreatedAtUtc', N'CreatedBy', N'IsActive', N'ModifiedAtUtc', N'Name', N'Sku', N'TaxCategory', N'UnitPrice') AND [object_id] = OBJECT_ID(N'[dbo].[Products]'))
        SET IDENTITY_INSERT [dbo].[Products] ON;
    EXEC(N'INSERT INTO [dbo].[Products] ([Id], [CategoryId], [CreatedAtUtc], [CreatedBy], [IsActive], [ModifiedAtUtc], [Name], [Sku], [TaxCategory], [UnitPrice])
    VALUES (1, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Small padded bag'', ''PKG-001'', ''Standard'', 2.95),
    (2, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Medium padded bag'', ''PKG-002'', ''Standard'', 3.95),
    (3, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Large padded bag'', ''PKG-003'', ''Standard'', 4.95),
    (4, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Small box (Size 1)'', ''PKG-004'', ''Standard'', 2.5),
    (5, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Medium box (Size 3)'', ''PKG-005'', ''Standard'', 3.75),
    (6, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Large box (Size 5)'', ''PKG-006'', ''Standard'', 5.25),
    (7, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Tube 1m'', ''PKG-007'', ''Standard'', 6.5),
    (8, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Bubble wrap roll 3m'', ''PKG-008'', ''Standard'', 7.95),
    (9, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Packing tape 50m'', ''PKG-009'', ''Standard'', 4.5),
    (10, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Prepaid satchel 500g'', ''PKG-010'', ''Standard'', 11.0),
    (11, 0, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Prepaid satchel 3kg'', ''PKG-011'', ''Standard'', 16.5),
    (12, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''DL envelopes (pack of 25)'', ''STN-001'', ''Standard'', 4.25),
    (13, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''C4 envelopes (pack of 10)'', ''STN-002'', ''Standard'', 5.5),
    (14, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Greeting card - Birthday'', ''STN-003'', ''Standard'', 5.95),
    (15, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Greeting card - Charity'', ''STN-004'', ''Exempt'', 4.95),
    (16, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Ballpoint pens (pack of 10)'', ''STN-005'', ''Standard'', 6.95),
    (17, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''A5 notebook'', ''STN-006'', ''Reduced'', 3.95),
    (18, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Exercise book 96pg'', ''STN-007'', ''Reduced'', 1.95),
    (19, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Postcard set (6)'', ''STN-008'', ''Standard'', 7.5),
    (20, 1, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Address labels (100)'', ''STN-009'', ''Standard'', 8.95),
    (21, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''A4 copy paper 500 sheets'', ''OFF-001'', ''Standard'', 8.5),
    (22, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Stapler'', ''OFF-002'', ''Standard'', 12.95),
    (23, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Staples (5000)'', ''OFF-003'', ''Standard'', 3.5),
    (24, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Lever arch file'', ''OFF-004'', ''Standard'', 4.95),
    (25, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Highlighters (4)'', ''OFF-005'', ''Standard'', 5.95),
    (26, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Sticky notes (12 pads)'', ''OFF-006'', ''Standard'', 9.95),
    (27, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''USB flash drive 64GB'', ''OFF-007'', ''Standard'', 19.95),
    (28, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Printer ink black'', ''OFF-008'', ''Standard'', 34.95),
    (29, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Calculator'', ''OFF-009'', ''Standard'', 14.95),
    (30, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Scissors'', ''OFF-010'', ''Standard'', 6.5),
    (31, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Desk organiser'', ''OFF-011'', ''Standard'', 17.95),
    (32, 2, ''2026-10-01T00:00:00.000Z'', ''seed'', CAST(1 AS bit), NULL, N''Document wallet (5)'', ''OFF-012'', ''Reduced'', 7.95)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CategoryId', N'CreatedAtUtc', N'CreatedBy', N'IsActive', N'ModifiedAtUtc', N'Name', N'Sku', N'TaxCategory', N'UnitPrice') AND [object_id] = OBJECT_ID(N'[dbo].[Products]'))
        SET IDENTITY_INSERT [dbo].[Products] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BaseRate', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'PerKgRate', N'ShippingType', N'ZoneCode') AND [object_id] = OBJECT_ID(N'[dbo].[ShippingRates]'))
        SET IDENTITY_INSERT [dbo].[ShippingRates] ON;
    EXEC(N'INSERT INTO [dbo].[ShippingRates] ([Id], [BaseRate], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [PerKgRate], [ShippingType], [ZoneCode])
    VALUES (1, 1.2, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z01''),
    (2, 8.95, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 1.1, ''Parcel'', ''Z01''),
    (3, 1.2, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z02''),
    (4, 10.45, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 1.5, ''Parcel'', ''Z02''),
    (5, 1.2, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z03''),
    (6, 12.95, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 2.1, ''Parcel'', ''Z03''),
    (7, 1.2, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z04''),
    (8, 14.95, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 2.6, ''Parcel'', ''Z04''),
    (9, 1.4, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z05''),
    (10, 16.95, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 3.2, ''Parcel'', ''Z05''),
    (11, 1.2, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z06''),
    (12, 13.45, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 2.3, ''Parcel'', ''Z06''),
    (13, 3.1, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z07''),
    (14, 21.5, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 4.8, ''Parcel'', ''Z07''),
    (15, 3.6, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z08''),
    (16, 27.5, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 6.9, ''Parcel'', ''Z08''),
    (17, 4.2, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z09''),
    (18, 34.95, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 9.4, ''Parcel'', ''Z09''),
    (19, 4.2, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 0.0, ''Letter'', ''Z10''),
    (20, 36.95, ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 9.9, ''Parcel'', ''Z10'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BaseRate', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'PerKgRate', N'ShippingType', N'ZoneCode') AND [object_id] = OBJECT_ID(N'[dbo].[ShippingRates]'))
        SET IDENTITY_INSERT [dbo].[ShippingRates] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAtUtc', N'CreatedBy', N'Email', N'HomeBranchCode', N'IsActive', N'ModifiedAtUtc', N'Name', N'PasswordHash', N'RoleId') AND [object_id] = OBJECT_ID(N'[dbo].[Staff]'))
        SET IDENTITY_INSERT [dbo].[Staff] ON;
    EXEC(N'INSERT INTO [dbo].[Staff] ([Id], [CreatedAtUtc], [CreatedBy], [Email], [HomeBranchCode], [IsActive], [ModifiedAtUtc], [Name], [PasswordHash], [RoleId])
    VALUES (1, ''2026-10-01T00:00:00.000Z'', ''seed'', ''admin@apexpost.local'', ''BR-001'', CAST(1 AS bit), NULL, N''Avery Admin'', ''PBKDF2$100000$1WIw9+8h6K5o76wd72maGw==$KZny1K7LyeKvOadi3GBxl0XCdJSG0+KkJNDCjKyHro4='', 3),
    (2, ''2026-10-01T00:00:00.000Z'', ''seed'', ''it.admin@apexpost.local'', ''BR-003'', CAST(1 AS bit), NULL, N''Jordan Systems'', ''PBKDF2$100000$CXBMqGxXRNAbgchZk2zHZA==$Pu38WljeYYPqfjBhfo92GXS0GJN3CXFHBtIJ44XlGRQ='', 3),
    (3, ''2026-10-01T00:00:00.000Z'', ''seed'', ''auditor@apexpost.local'', ''BR-001'', CAST(1 AS bit), NULL, N''Riley Auditor'', ''PBKDF2$100000$ozNg6QPAFAV4fsrFcQPiSg==$TJ1ktc3GcCRfq5Psg11Tnla3SeNwYgMNqE9Cm+OV1LY='', 2),
    (4, ''2026-10-01T00:00:00.000Z'', ''seed'', ''compliance.auditor@apexpost.local'', ''BR-003'', CAST(1 AS bit), NULL, N''Morgan Compliance'', ''PBKDF2$100000$d+XqtiIt9TlxJBTbae2zTQ==$QKardjd4NnZ2iGo1YBZ3GL5BX1JNpQXAlaTU6k1ZmaY='', 2),
    (5, ''2026-10-01T00:00:00.000Z'', ''seed'', ''review.auditor@apexpost.local'', ''BR-004'', CAST(1 AS bit), NULL, N''Casey Review'', ''PBKDF2$100000$epU5eevKmG2dU2RqAzvHxQ==$NYlnfDmg5uQ2GDnMaWSXlwo3X8M1D8lpHAaQMp/UEOU='', 2),
    (6, ''2026-10-01T00:00:00.000Z'', ''seed'', ''manager.br001@apexpost.local'', ''BR-001'', CAST(1 AS bit), NULL, N''Priya Sharma'', ''PBKDF2$100000$3vxPIs+dx4ZGdrhR67Dp0Q==$HeG/06HGcYGEf/KpePFjhXJrmLq+NOQbe1BEUAp/dAY='', 1),
    (7, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br001a@apexpost.local'', ''BR-001'', CAST(1 AS bit), NULL, N''Liam Rossi'', ''PBKDF2$100000$cZcsbbOw/SHSGlheKiweQg==$4EuwPX4//ofgPO+7FwZq6Szo9J4Xqqr9TfY+BDNH+Ms='', 0),
    (8, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br001b@apexpost.local'', ''BR-001'', CAST(1 AS bit), NULL, N''Mei Wilson'', ''PBKDF2$100000$Pa2tPnCIRfxEVW29X6mTBg==$AC7ZakHUeRJipE0dCfEiLtphxVm46zNmBasC22I42iw='', 0),
    (9, ''2026-10-01T00:00:00.000Z'', ''seed'', ''manager.br002@apexpost.local'', ''BR-002'', CAST(1 AS bit), NULL, N''Noah Smith'', ''PBKDF2$100000$io6/PT+dG376H257kN7MUA==$2z8BPp0jv03GHMvl9KV2SVPSgCWYxsMP1ev7Sk1eJmk='', 1),
    (10, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br002a@apexpost.local'', ''BR-002'', CAST(1 AS bit), NULL, N''Aisha Silva'', ''PBKDF2$100000$cD9wDOjXHw57LSzEf+PAGw==$zlZ0Wp00QrdcRfD6Pt2t3gle0rmSMJcdEDZMaW3m/dc='', 0),
    (11, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br002b@apexpost.local'', ''BR-002'', CAST(1 AS bit), NULL, N''Oliver Ali'', ''PBKDF2$100000$1l9wtwekEF8lWfToSppTrQ==$KcoxxsP15vXXxPIvsY30Bx2hEbBs1Grvqzrqoab2uko='', 0),
    (12, ''2026-10-01T00:00:00.000Z'', ''seed'', ''manager.br003@apexpost.local'', ''BR-003'', CAST(1 AS bit), NULL, N''Sofia Rossi'', ''PBKDF2$100000$VQWcRTjUmTuBX2p3pvqeMw==$nCipRjuZCnaoKzcXRkmVu7lSnftoGcy99S2NrMdYBHo='', 1),
    (13, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br003a@apexpost.local'', ''BR-003'', CAST(1 AS bit), NULL, N''Ethan Taylor'', ''PBKDF2$100000$KXZdY4w2z9SNpXAiDSeU6w==$I80J8YXng5Fxa2Bdl91XVRaOR7C4rktNiCalaxPgoT0='', 0),
    (14, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br003b@apexpost.local'', ''BR-003'', CAST(1 AS bit), NULL, N''Hana Patel'', ''PBKDF2$100000$B0eOJuXjCRrcIxrUtza8aA==$GBLj36BqhCyz/gBjCZ1Q8BE4+DgARdFav7esjwEVeWw='', 0),
    (15, ''2026-10-01T00:00:00.000Z'', ''seed'', ''manager.br004@apexpost.local'', ''BR-004'', CAST(1 AS bit), NULL, N''Lucas Silva'', ''PBKDF2$100000$ycIgbQwm7VmK/QIGZLi+Bw==$w2CsxVJi8dgustx8SKtf3M2hOEroXX+kP2zQU/bCAjw='', 1),
    (16, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br004a@apexpost.local'', ''BR-004'', CAST(1 AS bit), NULL, N''Zara Nguyen'', ''PBKDF2$100000$M8izjA6a9fcOgfbEisxNPg==$vB8kTR4KXY//av7qoGyhAMf/xAjbSd26f6CJF+IqIfs='', 0),
    (17, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br004b@apexpost.local'', ''BR-004'', CAST(1 AS bit), NULL, N''Mateo Chen'', ''PBKDF2$100000$fbri+al+23VUJ8V5j5BmIw==$B1f4j9zq5RRVG5ktbLg169buPsRBG0JHavS4DwP6v0s='', 0),
    (18, ''2026-10-01T00:00:00.000Z'', ''seed'', ''manager.br005@apexpost.local'', ''BR-005'', CAST(1 AS bit), NULL, N''Chloe Taylor'', ''PBKDF2$100000$1avwfuhVLWOgx0Wl545kQw==$ptwqOh+nvuIUmF6P2CQNk7rilxIuK7M6nVOOLVsMzCA='', 1),
    (19, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br005a@apexpost.local'', ''BR-005'', CAST(1 AS bit), NULL, N''Ravi Khan'', ''PBKDF2$100000$egVU22oPCEzeXzgH4FzAXA==$5DGkbvCpl0QW/wzyRRvdvwSQPONWe7kp7NQmChV7shQ='', 0),
    (20, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br005b@apexpost.local'', ''BR-005'', CAST(1 AS bit), NULL, N''Priya Brown'', ''PBKDF2$100000$3XlNIs5grpASr07Qq7CNZg==$mt1g0M0YZnMQ6giGUD+7ahonJ9VYO81WFs823VZomGc='', 0),
    (21, ''2026-10-01T00:00:00.000Z'', ''seed'', ''manager.br006@apexpost.local'', ''BR-006'', CAST(1 AS bit), NULL, N''Liam Nguyen'', ''PBKDF2$100000$4RNgPAv76P+swHrSV+1wNw==$xArh8MoP88ATzuaXyFqG9lysA/2ZJ/yGupjuUpRqs3M='', 1),
    (22, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br006a@apexpost.local'', ''BR-006'', CAST(1 AS bit), NULL, N''Mei Wilson'', ''PBKDF2$100000$O5dMYZslJ3kzQfz637oWZg==$98M7mBe7dWQM/0R5Ew61M27HZCgTfs9p1vM5iLOVz+U='', 0),
    (23, ''2026-10-01T00:00:00.000Z'', ''seed'', ''clerk.br006b@apexpost.local'', ''BR-006'', CAST(1 AS bit), NULL, N''Noah Sato'', ''PBKDF2$100000$08NH1yl4GUORdSyHHkI+mQ==$xkRZv8AT1/bbWErWLkED0q+MPrURzM1O2QAd/inZXxQ='', 0)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAtUtc', N'CreatedBy', N'Email', N'HomeBranchCode', N'IsActive', N'ModifiedAtUtc', N'Name', N'PasswordHash', N'RoleId') AND [object_id] = OBJECT_ID(N'[dbo].[Staff]'))
        SET IDENTITY_INSERT [dbo].[Staff] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAtUtc', N'CreatedBy', N'Email', N'HomeBranchCode', N'ModifiedAtUtc', N'Name', N'PasswordHash', N'RoleId') AND [object_id] = OBJECT_ID(N'[dbo].[Staff]'))
        SET IDENTITY_INSERT [dbo].[Staff] ON;
    EXEC(N'INSERT INTO [dbo].[Staff] ([Id], [CreatedAtUtc], [CreatedBy], [Email], [HomeBranchCode], [ModifiedAtUtc], [Name], [PasswordHash], [RoleId])
    VALUES (24, ''2026-10-01T00:00:00.000Z'', ''seed'', ''former.clerk@apexpost.local'', ''BR-002'', NULL, N''Sam Former'', ''PBKDF2$100000$iO8a8/lUHDryGTU7lIB2Eg==$Ln0nIbxJlcYfZehAWufRRDJ2pggz9xRbzhyUsaGLH5Y='', 0)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAtUtc', N'CreatedBy', N'Email', N'HomeBranchCode', N'ModifiedAtUtc', N'Name', N'PasswordHash', N'RoleId') AND [object_id] = OBJECT_ID(N'[dbo].[Staff]'))
        SET IDENTITY_INSERT [dbo].[Staff] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'QuantityOnHand', N'ReorderThreshold', N'Sku', N'UpdatedAtUtc') AND [object_id] = OBJECT_ID(N'[dbo].[BranchStock]'))
        SET IDENTITY_INSERT [dbo].[BranchStock] ON;
    EXEC(N'INSERT INTO [dbo].[BranchStock] ([Id], [BranchCode], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [QuantityOnHand], [ReorderThreshold], [Sku], [UpdatedAtUtc])
    VALUES (1, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 44, 5, ''PKG-001'', ''2026-10-25T07:00:00.000Z''),
    (2, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 66, 12, ''PKG-002'', ''2026-10-25T07:00:00.000Z''),
    (3, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 66, 5, ''PKG-003'', ''2026-10-25T07:00:00.000Z''),
    (4, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 58, 10, ''PKG-004'', ''2026-10-25T07:00:00.000Z''),
    (5, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 78, 10, ''PKG-005'', ''2026-10-25T07:00:00.000Z''),
    (6, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 21, 10, ''PKG-006'', ''2026-10-25T07:00:00.000Z''),
    (7, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 78, 10, ''PKG-007'', ''2026-10-25T07:00:00.000Z''),
    (8, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 78, 8, ''PKG-008'', ''2026-10-25T07:00:00.000Z''),
    (9, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 67, 12, ''PKG-009'', ''2026-10-25T07:00:00.000Z''),
    (10, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 42, 10, ''PKG-010'', ''2026-10-25T07:00:00.000Z''),
    (11, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 70, 12, ''PKG-011'', ''2026-10-25T07:00:00.000Z''),
    (12, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 58, 5, ''STN-001'', ''2026-10-25T07:00:00.000Z''),
    (13, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 33, 10, ''STN-002'', ''2026-10-25T07:00:00.000Z''),
    (14, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 76, 10, ''STN-003'', ''2026-10-25T07:00:00.000Z''),
    (15, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 58, 12, ''STN-004'', ''2026-10-25T07:00:00.000Z''),
    (16, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 34, 5, ''STN-005'', ''2026-10-25T07:00:00.000Z''),
    (17, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 43, 8, ''STN-006'', ''2026-10-25T07:00:00.000Z''),
    (18, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 22, 8, ''STN-007'', ''2026-10-25T07:00:00.000Z''),
    (19, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 29, 12, ''STN-008'', ''2026-10-25T07:00:00.000Z''),
    (20, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 79, 5, ''STN-009'', ''2026-10-25T07:00:00.000Z''),
    (21, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 23, 8, ''OFF-001'', ''2026-10-25T07:00:00.000Z''),
    (22, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 65, 8, ''OFF-002'', ''2026-10-25T07:00:00.000Z''),
    (23, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 50, 10, ''OFF-003'', ''2026-10-25T07:00:00.000Z''),
    (24, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 60, 5, ''OFF-004'', ''2026-10-25T07:00:00.000Z''),
    (25, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 52, 10, ''OFF-005'', ''2026-10-25T07:00:00.000Z''),
    (26, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 25, 12, ''OFF-006'', ''2026-10-25T07:00:00.000Z''),
    (27, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 16, 10, ''OFF-007'', ''2026-10-25T07:00:00.000Z''),
    (28, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 25, 10, ''OFF-008'', ''2026-10-25T07:00:00.000Z''),
    (29, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 16, 5, ''OFF-009'', ''2026-10-25T07:00:00.000Z''),
    (30, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 29, 5, ''OFF-010'', ''2026-10-25T07:00:00.000Z''),
    (31, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 38, 10, ''OFF-011'', ''2026-10-25T07:00:00.000Z''),
    (32, ''BR-001'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 1, 12, ''OFF-012'', ''2026-10-25T07:00:00.000Z''),
    (33, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 51, 12, ''PKG-001'', ''2026-10-25T07:00:00.000Z''),
    (34, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 70, 12, ''PKG-002'', ''2026-10-25T07:00:00.000Z''),
    (35, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 53, 10, ''PKG-003'', ''2026-10-25T07:00:00.000Z''),
    (36, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 69, 12, ''PKG-004'', ''2026-10-25T07:00:00.000Z''),
    (37, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 25, 12, ''PKG-005'', ''2026-10-25T07:00:00.000Z''),
    (38, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 57, 10, ''PKG-006'', ''2026-10-25T07:00:00.000Z''),
    (39, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 19, 5, ''PKG-007'', ''2026-10-25T07:00:00.000Z''),
    (40, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 60, 10, ''PKG-008'', ''2026-10-25T07:00:00.000Z''),
    (41, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 53, 10, ''PKG-009'', ''2026-10-25T07:00:00.000Z''),
    (42, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 53, 10, ''PKG-010'', ''2026-10-25T07:00:00.000Z'');
    INSERT INTO [dbo].[BranchStock] ([Id], [BranchCode], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [QuantityOnHand], [ReorderThreshold], [Sku], [UpdatedAtUtc])
    VALUES (43, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 65, 10, ''PKG-011'', ''2026-10-25T07:00:00.000Z''),
    (44, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 73, 12, ''STN-001'', ''2026-10-25T07:00:00.000Z''),
    (45, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 69, 10, ''STN-002'', ''2026-10-25T07:00:00.000Z''),
    (46, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 33, 10, ''STN-003'', ''2026-10-25T07:00:00.000Z''),
    (47, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 73, 10, ''STN-004'', ''2026-10-25T07:00:00.000Z''),
    (48, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 64, 8, ''STN-005'', ''2026-10-25T07:00:00.000Z''),
    (49, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 33, 12, ''STN-006'', ''2026-10-25T07:00:00.000Z''),
    (50, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 47, 12, ''STN-007'', ''2026-10-25T07:00:00.000Z''),
    (51, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 61, 12, ''STN-008'', ''2026-10-25T07:00:00.000Z''),
    (52, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 43, 12, ''STN-009'', ''2026-10-25T07:00:00.000Z''),
    (53, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 60, 10, ''OFF-001'', ''2026-10-25T07:00:00.000Z''),
    (54, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 58, 8, ''OFF-002'', ''2026-10-25T07:00:00.000Z''),
    (55, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 52, 12, ''OFF-003'', ''2026-10-25T07:00:00.000Z''),
    (56, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 26, 5, ''OFF-004'', ''2026-10-25T07:00:00.000Z''),
    (57, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 29, 10, ''OFF-005'', ''2026-10-25T07:00:00.000Z''),
    (58, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 32, 12, ''OFF-006'', ''2026-10-25T07:00:00.000Z''),
    (59, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 3, 5, ''OFF-007'', ''2026-10-25T07:00:00.000Z''),
    (60, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 58, 8, ''OFF-008'', ''2026-10-25T07:00:00.000Z''),
    (61, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 42, 5, ''OFF-009'', ''2026-10-25T07:00:00.000Z''),
    (62, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 8, 10, ''OFF-010'', ''2026-10-25T07:00:00.000Z''),
    (63, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 46, 5, ''OFF-011'', ''2026-10-25T07:00:00.000Z''),
    (64, ''BR-002'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 7, 8, ''OFF-012'', ''2026-10-25T07:00:00.000Z''),
    (65, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 24, 8, ''PKG-001'', ''2026-10-25T07:00:00.000Z''),
    (66, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 69, 5, ''PKG-002'', ''2026-10-25T07:00:00.000Z''),
    (67, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 76, 10, ''PKG-003'', ''2026-10-25T07:00:00.000Z''),
    (68, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 57, 5, ''PKG-004'', ''2026-10-25T07:00:00.000Z''),
    (69, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 77, 8, ''PKG-005'', ''2026-10-25T07:00:00.000Z''),
    (70, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 63, 12, ''PKG-006'', ''2026-10-25T07:00:00.000Z''),
    (71, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 25, 12, ''PKG-007'', ''2026-10-25T07:00:00.000Z''),
    (72, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 15, 8, ''PKG-008'', ''2026-10-25T07:00:00.000Z''),
    (73, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 18, 5, ''PKG-009'', ''2026-10-25T07:00:00.000Z''),
    (74, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 4, 5, ''PKG-010'', ''2026-10-25T07:00:00.000Z''),
    (75, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 33, 5, ''PKG-011'', ''2026-10-25T07:00:00.000Z''),
    (76, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 46, 12, ''STN-001'', ''2026-10-25T07:00:00.000Z''),
    (77, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 34, 5, ''STN-002'', ''2026-10-25T07:00:00.000Z''),
    (78, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 49, 10, ''STN-003'', ''2026-10-25T07:00:00.000Z''),
    (79, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 72, 5, ''STN-004'', ''2026-10-25T07:00:00.000Z''),
    (80, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 45, 10, ''STN-005'', ''2026-10-25T07:00:00.000Z''),
    (81, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 28, 10, ''STN-006'', ''2026-10-25T07:00:00.000Z''),
    (82, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 57, 12, ''STN-007'', ''2026-10-25T07:00:00.000Z''),
    (83, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 64, 8, ''STN-008'', ''2026-10-25T07:00:00.000Z''),
    (84, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 39, 12, ''STN-009'', ''2026-10-25T07:00:00.000Z'');
    INSERT INTO [dbo].[BranchStock] ([Id], [BranchCode], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [QuantityOnHand], [ReorderThreshold], [Sku], [UpdatedAtUtc])
    VALUES (85, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 39, 5, ''OFF-001'', ''2026-10-25T07:00:00.000Z''),
    (86, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 39, 8, ''OFF-002'', ''2026-10-25T07:00:00.000Z''),
    (87, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 69, 10, ''OFF-003'', ''2026-10-25T07:00:00.000Z''),
    (88, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 50, 10, ''OFF-004'', ''2026-10-25T07:00:00.000Z''),
    (89, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 79, 10, ''OFF-005'', ''2026-10-25T07:00:00.000Z''),
    (90, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 19, 5, ''OFF-006'', ''2026-10-25T07:00:00.000Z''),
    (91, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 17, 12, ''OFF-007'', ''2026-10-25T07:00:00.000Z''),
    (92, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 67, 12, ''OFF-008'', ''2026-10-25T07:00:00.000Z''),
    (93, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 37, 12, ''OFF-009'', ''2026-10-25T07:00:00.000Z''),
    (94, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 63, 8, ''OFF-010'', ''2026-10-25T07:00:00.000Z''),
    (95, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 2, 5, ''OFF-011'', ''2026-10-25T07:00:00.000Z''),
    (96, ''BR-003'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 1, 12, ''OFF-012'', ''2026-10-25T07:00:00.000Z''),
    (97, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 71, 10, ''PKG-001'', ''2026-10-25T07:00:00.000Z''),
    (98, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 36, 10, ''PKG-002'', ''2026-10-25T07:00:00.000Z''),
    (99, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 28, 5, ''PKG-003'', ''2026-10-25T07:00:00.000Z''),
    (100, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 70, 5, ''PKG-004'', ''2026-10-25T07:00:00.000Z''),
    (101, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 6, 10, ''PKG-005'', ''2026-10-25T07:00:00.000Z''),
    (102, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 40, 12, ''PKG-006'', ''2026-10-25T07:00:00.000Z''),
    (103, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 51, 5, ''PKG-007'', ''2026-10-25T07:00:00.000Z''),
    (104, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 22, 10, ''PKG-008'', ''2026-10-25T07:00:00.000Z''),
    (105, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 35, 12, ''PKG-009'', ''2026-10-25T07:00:00.000Z''),
    (106, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 34, 8, ''PKG-010'', ''2026-10-25T07:00:00.000Z''),
    (107, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 31, 12, ''PKG-011'', ''2026-10-25T07:00:00.000Z''),
    (108, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 56, 10, ''STN-001'', ''2026-10-25T07:00:00.000Z''),
    (109, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 22, 5, ''STN-002'', ''2026-10-25T07:00:00.000Z''),
    (110, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 37, 5, ''STN-003'', ''2026-10-25T07:00:00.000Z''),
    (111, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 44, 12, ''STN-004'', ''2026-10-25T07:00:00.000Z''),
    (112, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 29, 5, ''STN-005'', ''2026-10-25T07:00:00.000Z''),
    (113, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 59, 5, ''STN-006'', ''2026-10-25T07:00:00.000Z''),
    (114, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 78, 8, ''STN-007'', ''2026-10-25T07:00:00.000Z''),
    (115, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 78, 5, ''STN-008'', ''2026-10-25T07:00:00.000Z''),
    (116, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 71, 10, ''STN-009'', ''2026-10-25T07:00:00.000Z''),
    (117, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 50, 8, ''OFF-001'', ''2026-10-25T07:00:00.000Z''),
    (118, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 27, 8, ''OFF-002'', ''2026-10-25T07:00:00.000Z''),
    (119, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 69, 12, ''OFF-003'', ''2026-10-25T07:00:00.000Z''),
    (120, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 20, 5, ''OFF-004'', ''2026-10-25T07:00:00.000Z''),
    (121, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 54, 5, ''OFF-005'', ''2026-10-25T07:00:00.000Z''),
    (122, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 55, 8, ''OFF-006'', ''2026-10-25T07:00:00.000Z''),
    (123, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 48, 8, ''OFF-007'', ''2026-10-25T07:00:00.000Z''),
    (124, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 68, 5, ''OFF-008'', ''2026-10-25T07:00:00.000Z''),
    (125, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 80, 12, ''OFF-009'', ''2026-10-25T07:00:00.000Z''),
    (126, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 31, 8, ''OFF-010'', ''2026-10-25T07:00:00.000Z'');
    INSERT INTO [dbo].[BranchStock] ([Id], [BranchCode], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [QuantityOnHand], [ReorderThreshold], [Sku], [UpdatedAtUtc])
    VALUES (127, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 42, 12, ''OFF-011'', ''2026-10-25T07:00:00.000Z''),
    (128, ''BR-004'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 20, 10, ''OFF-012'', ''2026-10-25T07:00:00.000Z''),
    (129, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 75, 5, ''PKG-001'', ''2026-10-25T07:00:00.000Z''),
    (130, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 15, 12, ''PKG-002'', ''2026-10-25T07:00:00.000Z''),
    (131, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 70, 10, ''PKG-003'', ''2026-10-25T07:00:00.000Z''),
    (132, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 4, 5, ''PKG-004'', ''2026-10-25T07:00:00.000Z''),
    (133, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 77, 10, ''PKG-005'', ''2026-10-25T07:00:00.000Z''),
    (134, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 5, 12, ''PKG-006'', ''2026-10-25T07:00:00.000Z''),
    (135, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 53, 10, ''PKG-007'', ''2026-10-25T07:00:00.000Z''),
    (136, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 44, 5, ''PKG-008'', ''2026-10-25T07:00:00.000Z''),
    (137, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 26, 10, ''PKG-009'', ''2026-10-25T07:00:00.000Z''),
    (138, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 28, 12, ''PKG-010'', ''2026-10-25T07:00:00.000Z''),
    (139, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 25, 8, ''PKG-011'', ''2026-10-25T07:00:00.000Z''),
    (140, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 59, 10, ''STN-001'', ''2026-10-25T07:00:00.000Z''),
    (141, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 64, 5, ''STN-002'', ''2026-10-25T07:00:00.000Z''),
    (142, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 16, 12, ''STN-003'', ''2026-10-25T07:00:00.000Z''),
    (143, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 20, 5, ''STN-004'', ''2026-10-25T07:00:00.000Z''),
    (144, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 73, 8, ''STN-005'', ''2026-10-25T07:00:00.000Z''),
    (145, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 72, 12, ''STN-006'', ''2026-10-25T07:00:00.000Z''),
    (146, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 55, 5, ''STN-007'', ''2026-10-25T07:00:00.000Z''),
    (147, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 2, 5, ''STN-008'', ''2026-10-25T07:00:00.000Z''),
    (148, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 74, 8, ''STN-009'', ''2026-10-25T07:00:00.000Z''),
    (149, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 48, 8, ''OFF-001'', ''2026-10-25T07:00:00.000Z''),
    (150, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 21, 8, ''OFF-002'', ''2026-10-25T07:00:00.000Z''),
    (151, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 4, 10, ''OFF-003'', ''2026-10-25T07:00:00.000Z''),
    (152, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 34, 10, ''OFF-004'', ''2026-10-25T07:00:00.000Z''),
    (153, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 25, 10, ''OFF-005'', ''2026-10-25T07:00:00.000Z''),
    (154, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 63, 10, ''OFF-006'', ''2026-10-25T07:00:00.000Z''),
    (155, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 76, 8, ''OFF-007'', ''2026-10-25T07:00:00.000Z''),
    (156, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 56, 8, ''OFF-008'', ''2026-10-25T07:00:00.000Z''),
    (157, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 48, 5, ''OFF-009'', ''2026-10-25T07:00:00.000Z''),
    (158, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 79, 10, ''OFF-010'', ''2026-10-25T07:00:00.000Z''),
    (159, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 5, 8, ''OFF-011'', ''2026-10-25T07:00:00.000Z''),
    (160, ''BR-005'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 70, 10, ''OFF-012'', ''2026-10-25T07:00:00.000Z''),
    (161, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 6, 10, ''PKG-001'', ''2026-10-25T07:00:00.000Z''),
    (162, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 39, 10, ''PKG-002'', ''2026-10-25T07:00:00.000Z''),
    (163, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 2, 12, ''PKG-003'', ''2026-10-25T07:00:00.000Z''),
    (164, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 23, 5, ''PKG-004'', ''2026-10-25T07:00:00.000Z''),
    (165, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 17, 12, ''PKG-005'', ''2026-10-25T07:00:00.000Z''),
    (166, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 66, 8, ''PKG-006'', ''2026-10-25T07:00:00.000Z''),
    (167, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 9, 12, ''PKG-007'', ''2026-10-25T07:00:00.000Z''),
    (168, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 33, 8, ''PKG-008'', ''2026-10-25T07:00:00.000Z'');
    INSERT INTO [dbo].[BranchStock] ([Id], [BranchCode], [CreatedAtUtc], [CreatedBy], [ModifiedAtUtc], [QuantityOnHand], [ReorderThreshold], [Sku], [UpdatedAtUtc])
    VALUES (169, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 71, 12, ''PKG-009'', ''2026-10-25T07:00:00.000Z''),
    (170, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 30, 5, ''PKG-010'', ''2026-10-25T07:00:00.000Z''),
    (171, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 80, 10, ''PKG-011'', ''2026-10-25T07:00:00.000Z''),
    (172, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 36, 5, ''STN-001'', ''2026-10-25T07:00:00.000Z''),
    (173, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 66, 10, ''STN-002'', ''2026-10-25T07:00:00.000Z''),
    (174, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 73, 12, ''STN-003'', ''2026-10-25T07:00:00.000Z''),
    (175, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 32, 5, ''STN-004'', ''2026-10-25T07:00:00.000Z''),
    (176, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 34, 8, ''STN-005'', ''2026-10-25T07:00:00.000Z''),
    (177, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 38, 12, ''STN-006'', ''2026-10-25T07:00:00.000Z''),
    (178, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 25, 12, ''STN-007'', ''2026-10-25T07:00:00.000Z''),
    (179, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 50, 10, ''STN-008'', ''2026-10-25T07:00:00.000Z''),
    (180, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 67, 5, ''STN-009'', ''2026-10-25T07:00:00.000Z''),
    (181, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 77, 8, ''OFF-001'', ''2026-10-25T07:00:00.000Z''),
    (182, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 62, 5, ''OFF-002'', ''2026-10-25T07:00:00.000Z''),
    (183, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 34, 10, ''OFF-003'', ''2026-10-25T07:00:00.000Z''),
    (184, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 67, 8, ''OFF-004'', ''2026-10-25T07:00:00.000Z''),
    (185, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 15, 12, ''OFF-005'', ''2026-10-25T07:00:00.000Z''),
    (186, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 71, 10, ''OFF-006'', ''2026-10-25T07:00:00.000Z''),
    (187, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 4, 10, ''OFF-007'', ''2026-10-25T07:00:00.000Z''),
    (188, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 28, 10, ''OFF-008'', ''2026-10-25T07:00:00.000Z''),
    (189, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 75, 5, ''OFF-009'', ''2026-10-25T07:00:00.000Z''),
    (190, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 68, 5, ''OFF-010'', ''2026-10-25T07:00:00.000Z''),
    (191, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 33, 10, ''OFF-011'', ''2026-10-25T07:00:00.000Z''),
    (192, ''BR-006'', ''2026-10-01T00:00:00.000Z'', ''seed'', NULL, 45, 12, ''OFF-012'', ''2026-10-25T07:00:00.000Z'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedAtUtc', N'CreatedBy', N'ModifiedAtUtc', N'QuantityOnHand', N'ReorderThreshold', N'Sku', N'UpdatedAtUtc') AND [object_id] = OBJECT_ID(N'[dbo].[BranchStock]'))
        SET IDENTITY_INSERT [dbo].[BranchStock] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE CLUSTERED INDEX [CIX_AuditEntries_TimestampUtc] ON [dbo].[AuditEntries] ([TimestampUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_Branch_Timestamp] ON [dbo].[AuditEntries] ([BranchCode], [TimestampUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BranchStock_Sku] ON [dbo].[BranchStock] ([Sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CounterTransactions_AuthorisedByStaffId] ON [dbo].[CounterTransactions] ([AuthorisedByStaffId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CounterTransactions_Branch_Counter] ON [dbo].[CounterTransactions] ([BranchCode], [CounterNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CounterTransactions_ClerkStaffId] ON [dbo].[CounterTransactions] ([ClerkStaffId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IdentityVerifications_Branch_VerifiedAt] ON [dbo].[IdentityVerifications] ([BranchCode], [VerifiedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IdentityVerifications_StaffId] ON [dbo].[IdentityVerifications] ([StaffId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IdentityVerifications_TransactionId] ON [dbo].[IdentityVerifications] ([TransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_IdentityVerifications_Reference] ON [dbo].[IdentityVerifications] ([Reference]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MoneyOrders_BranchCode] ON [dbo].[MoneyOrders] ([BranchCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MoneyOrders_IdentityVerificationId] ON [dbo].[MoneyOrders] ([IdentityVerificationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MoneyOrders_TransactionId] ON [dbo].[MoneyOrders] ([TransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_MoneyOrders_SerialNumber] ON [dbo].[MoneyOrders] ([SerialNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_Branch_RaisedAt] ON [dbo].[Notifications] ([BranchCode], [RaisedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Payments_TransactionId] ON [dbo].[Payments] ([TransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_ProductCategories_Name] ON [dbo].[ProductCategories] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_CategoryId] ON [dbo].[Products] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Roles_Name] ON [dbo].[Roles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Shipments_BranchCode] ON [dbo].[Shipments] ([BranchCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Shipments_TransactionId] ON [dbo].[Shipments] ([TransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Shipments_ZoneCode] ON [dbo].[Shipments] ([ZoneCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Shipments_TrackingNumber] ON [dbo].[Shipments] ([TrackingNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_ShippingRates_Zone_Type] ON [dbo].[ShippingRates] ([ZoneCode], [ShippingType]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Staff_HomeBranchCode] ON [dbo].[Staff] ([HomeBranchCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Staff_RoleId] ON [dbo].[Staff] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Staff_Email] ON [dbo].[Staff] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockAdjustments_Branch_Sku_ChangedAt] ON [dbo].[StockAdjustments] ([BranchCode], [Sku], [ChangedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_SyncConflicts_Open] ON [dbo].[SyncConflicts] ([Status]) WHERE [Status] = ''Open''');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SyncConflicts_SyncInboxEntryId] ON [dbo].[SyncConflicts] ([SyncInboxEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SyncInbox_BranchCode] ON [dbo].[SyncInbox] ([BranchCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SyncInbox_ServerTransactionId] ON [dbo].[SyncInbox] ([ServerTransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_SyncInbox_Device_LocalTransaction] ON [dbo].[SyncInbox] ([DeviceId], [LocalTransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TransactionLines_LineType_ItemCode] ON [dbo].[TransactionLines] ([LineType], [ItemCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124101_InitialCreate'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030124101_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124132_AddReportingIndexes'
)
BEGIN
    CREATE INDEX [IX_Shipments_Zone_Level_QuotedAt] ON [dbo].[Shipments] ([ZoneCode], [ServiceLevel], [QuotedAtUtc] DESC) INCLUDE ([Status], [WeightKg], [Price], [BranchCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124132_AddReportingIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_MoneyOrders_Branch_RequestedAt_Audited] ON [dbo].[MoneyOrders] ([BranchCode], [RequestedAtUtc]) INCLUDE ([Amount], [Fee], [Status], [SerialNumber], [IdentityVerificationId]) WHERE [Status] IN (''Issued'', ''Cancelled'')');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124132_AddReportingIndexes'
)
BEGIN
    CREATE INDEX [IX_CounterTransactions_Branch_BusinessDate] ON [dbo].[CounterTransactions] ([BranchCode], [BusinessDate]) INCLUDE ([Status], [Subtotal], [TaxAmount], [PassThroughAmount], [TotalAmount]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124132_AddReportingIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_CounterTransactions_ClosedAt_Paid] ON [dbo].[CounterTransactions] ([ClosedAtUtc]) INCLUDE ([BranchCode], [TotalAmount]) WHERE [Status] = ''Paid''');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124132_AddReportingIndexes'
)
BEGIN
    CREATE INDEX [IX_BranchStock_Branch_Covering] ON [dbo].[BranchStock] ([BranchCode]) INCLUDE ([Sku], [QuantityOnHand], [ReorderThreshold]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124132_AddReportingIndexes'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030124132_AddReportingIndexes', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124158_AddStoredProcedures'
)
BEGIN
    EXEC(N'/*
      usp_DailyBranchSales: retail vs services totals for one branch and one business day.

      Why a procedure (ADR-W3-03): this is a fixed, DBA-owned report contract used by the dashboard,
      the finance export and SSMS users alike. The split needs a conditional aggregate over
      TransactionLines joined to CounterTransactions; keeping it in one place means every consumer gets
      the same definition of "retail" and "services", and the plan is tuned once against
      IX_CounterTransactions_Branch_BusinessDate (seek on BranchCode + BusinessDate, a SARGable
      equality on the persisted computed date column).
    */
    CREATE OR ALTER PROCEDURE dbo.usp_DailyBranchSales
        @BranchCode varchar(10),
        @Date       date
    AS
    BEGIN
        SET NOCOUNT ON;

        IF @BranchCode IS NULL OR @BranchCode NOT LIKE ''BR-[0-9][0-9][0-9]''
            THROW 50001, N''@BranchCode must be a branch code such as BR-001.'', 1;
        IF @Date IS NULL
            THROW 50001, N''@Date is required.'', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
            THROW 50002, N''Branch not found.'', 1;

        SELECT @BranchCode                                         AS BranchCode,
               @Date                                               AS BusinessDate,
               COUNT(DISTINCT t.Id)                                AS PaidTransactions,
               COALESCE(SUM(CASE WHEN l.LineType = ''RetailItem''  THEN l.LineTotal END), 0) AS RetailTotal,
               COALESCE(SUM(CASE WHEN l.LineType <> ''RetailItem'' THEN l.LineTotal END), 0) AS ServicesTotal,
               COALESCE(SUM(l.TaxAmount), 0)                       AS TaxTotal,
               COALESCE(SUM(l.LineTotal + l.TaxAmount + l.PassThroughAmount), 0) AS GrandTotal
        FROM dbo.CounterTransactions AS t
        JOIN dbo.TransactionLines    AS l ON l.TransactionId = t.Id
        WHERE t.BranchCode   = @BranchCode
          AND t.BusinessDate = @Date
          AND t.Status       = ''Paid'';
    END;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124158_AddStoredProcedures'
)
BEGIN
    EXEC(N'/*
      usp_LowStockReport: products at or below max(item reorder threshold, @Threshold).
      Both parameters are optional (NULL = all branches / the default threshold of 5).

      OPTION (RECOMPILE) vs alternatives (ADR-W3-03):
      An optional-parameter ("catch-all") predicate such as (@BranchCode IS NULL OR BranchCode = @BranchCode)
      gets ONE cached plan that must be correct for both shapes, so SQL Server scans. With RECOMPILE the
      optimizer sees the actual values on each call ("parameter embedding"), removes the dead branch
      of the OR and can SEEK IX_BranchStock_Branch_Covering for a single branch. The price is a compile
      on every call (~1 ms here). That is acceptable because this report runs a few times a day per
      manager, not thousands of times a second. Alternatives considered: two procedures (one per
      shape) or dynamic SQL with sp_executesql (two cached plans, but harder to read and review).
    */
    CREATE OR ALTER PROCEDURE dbo.usp_LowStockReport
        @BranchCode varchar(10) = NULL,
        @Threshold  int         = NULL
    AS
    BEGIN
        SET NOCOUNT ON;

        IF @BranchCode IS NOT NULL AND @BranchCode NOT LIKE ''BR-[0-9][0-9][0-9]''
            THROW 50001, N''@BranchCode must be NULL or a branch code such as BR-001.'', 1;
        IF @Threshold IS NOT NULL AND (@Threshold < 0 OR @Threshold > 1000)
            THROW 50001, N''@Threshold must be between 0 and 1000.'', 1;

        SET @Threshold = COALESCE(@Threshold, 5);

        SELECT s.BranchCode,
               s.Sku,
               p.Name       AS ProductName,
               p.CategoryId,
               s.QuantityOnHand,
               s.ReorderThreshold
        FROM dbo.BranchStock AS s
        JOIN dbo.Products    AS p ON p.Sku = s.Sku
        WHERE (@BranchCode IS NULL OR s.BranchCode = @BranchCode)
          AND s.QuantityOnHand <= CASE WHEN s.ReorderThreshold > @Threshold THEN s.ReorderThreshold ELSE @Threshold END
        ORDER BY s.BranchCode, s.QuantityOnHand, s.Sku
        OPTION (RECOMPILE);
    END;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124158_AddStoredProcedures'
)
BEGIN
    EXEC(N'/*
      usp_AdjustStock: manual stock adjustment (receive or write off) plus its history row.

      Why a procedure (ADR-W3-03): the read-check-write and the history insert must be atomic and
      must hold the right lock. UPDLOCK + HOLDLOCK on the stock row serialises concurrent adjustments
      of the same SKU at the same branch without blocking other rows; XACT_ABORT guarantees that any
      error (including a CHECK violation) rolls the whole thing back. Done in LINQ it would take two
      round trips inside an explicit transaction and could not take the update lock on the first read.
      Called from EF Core with SqlQuery and an interpolated FormattableString (parameters only).
    */
    CREATE OR ALTER PROCEDURE dbo.usp_AdjustStock
        @BranchCode varchar(10),
        @Sku        varchar(20),
        @Delta      int,
        @Reason     nvarchar(200),
        @ChangedBy  varchar(50)
    AS
    BEGIN
        SET NOCOUNT ON;
        SET XACT_ABORT ON;

        IF @BranchCode IS NULL OR @Sku IS NULL OR @Delta IS NULL OR @Delta = 0
            THROW 50001, N''@BranchCode, @Sku and a non-zero @Delta are required.'', 1;
        IF @Delta NOT BETWEEN -1000 AND 1000
            THROW 50001, N''@Delta must be between -1000 and 1000.'', 1;
        IF NULLIF(LTRIM(RTRIM(@Reason)), N'''') IS NULL
            THROW 50001, N''@Reason is required.'', 1;
        IF NULLIF(LTRIM(RTRIM(@ChangedBy)), '''') IS NULL
            THROW 50001, N''@ChangedBy is required.'', 1;

        BEGIN TRY
            BEGIN TRANSACTION;

            DECLARE @Current int;

            SELECT @Current = QuantityOnHand
            FROM dbo.BranchStock WITH (UPDLOCK, HOLDLOCK)
            WHERE BranchCode = @BranchCode AND Sku = @Sku;

            IF @Current IS NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
                   OR NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Sku = @Sku)
                    THROW 50002, N''Unknown branch or SKU.'', 1;
                IF @Delta < 0
                    THROW 50003, N''There is no stock of this SKU at this branch to write off.'', 1;

                INSERT dbo.BranchStock (BranchCode, Sku, QuantityOnHand, ReorderThreshold, UpdatedAtUtc, CreatedBy)
                VALUES (@BranchCode, @Sku, @Delta, 5, SYSUTCDATETIME(), @ChangedBy);
                SET @Current = 0;
            END
            ELSE
            BEGIN
                IF @Current + @Delta < 0
                    THROW 50003, N''The adjustment would take stock below zero.'', 1;

                UPDATE dbo.BranchStock
                SET QuantityOnHand = QuantityOnHand + @Delta,
                    UpdatedAtUtc   = SYSUTCDATETIME(),
                    ModifiedAtUtc  = SYSUTCDATETIME()
                WHERE BranchCode = @BranchCode AND Sku = @Sku;
            END;

            INSERT dbo.StockAdjustments (BranchCode, Sku, Delta, QuantityAfter, Reason, ChangedBy)
            VALUES (@BranchCode, @Sku, @Delta, @Current + @Delta, @Reason, @ChangedBy);

            DECLARE @AdjustmentId int = SCOPE_IDENTITY();
            COMMIT TRANSACTION;

            SELECT @Current + @Delta AS QuantityAfter, @AdjustmentId AS StockAdjustmentId;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
    END;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124158_AddStoredProcedures'
)
BEGIN
    /*
      Least-privilege database role for the web application (ADR-W3-03, README "Security").
      The app login is a member of apexpost_app only. It can read and write rows and execute the
      procedures, but cannot change the schema, cannot delete history and is not db_owner.
      Migrations are applied by a separate deployment login (db_ddladmin) or by an administrator.
    */
    IF DATABASE_PRINCIPAL_ID(N'apexpost_app') IS NULL
        CREATE ROLE apexpost_app AUTHORIZATION dbo;

    GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO apexpost_app;
    GRANT EXECUTE ON OBJECT::dbo.usp_DailyBranchSales TO apexpost_app;
    GRANT EXECUTE ON OBJECT::dbo.usp_LowStockReport   TO apexpost_app;
    GRANT EXECUTE ON OBJECT::dbo.usp_AdjustStock      TO apexpost_app;

    -- History and the audit trail are append-only for the application.
    DENY UPDATE, DELETE ON OBJECT::dbo.AuditEntries     TO apexpost_app;
    DENY UPDATE, DELETE ON OBJECT::dbo.StockAdjustments TO apexpost_app;
    DENY UPDATE, DELETE ON OBJECT::dbo.SyncInbox        TO apexpost_app;
    DENY DELETE ON OBJECT::dbo.CounterTransactions      TO apexpost_app;
    DENY DELETE ON OBJECT::dbo.Payments                 TO apexpost_app;

    -- Example (run once per environment, password from your secret store, never from source control):
    --   CREATE LOGIN apexpost_web WITH PASSWORD = '<from secret store>';
    --   CREATE USER apexpost_web FOR LOGIN apexpost_web;
    --   ALTER ROLE apexpost_app ADD MEMBER apexpost_web;

END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124158_AddStoredProcedures'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030124158_AddStoredProcedures', N'10.0.14');
END;

COMMIT;
GO

