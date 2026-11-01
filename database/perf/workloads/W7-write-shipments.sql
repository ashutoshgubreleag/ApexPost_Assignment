-- W7  WRITE COST on Shipments (IX_Shipments_Zone_Level_QuotedAt, a wide covering index).
-- Inserts @Rows shipments (copies with new tracking numbers) and rolls back, like W6.
--@ Rows int 0 20000
SET XACT_ABORT ON;
BEGIN TRANSACTION;
INSERT INTO dbo.Shipments
    (TrackingNumber, TransactionId, BranchCode, ShippingType, ServiceLevel, ZoneCode, WeightKg, ChargeableWeightKg,
     Price, Status, QuotedAtUtc, HeightCm, LengthCm, WidthCm, CreatedBy)
SELECT TOP (@Rows) 'WR' + RIGHT('000000000' + CAST(Id AS varchar(12)), 12), TransactionId, BranchCode, ShippingType, ServiceLevel, ZoneCode,
       WeightKg, ChargeableWeightKg, Price, Status, QuotedAtUtc, HeightCm, LengthCm, WidthCm, 'perf-write'
FROM dbo.Shipments
ORDER BY Id;
ROLLBACK TRANSACTION;
