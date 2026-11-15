using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Tracking;
using ApexPost.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.IntegrationTests.Tracking;

/// <summary>One migrated SQL Server database shared by the tests of one class (creating one takes a few seconds).</summary>
public sealed class TrackingDatabaseFixture : IAsyncLifetime
{
    private int _next;

    public ServiceHarness Harness { get; private set; } = null!;

    /// <summary>
    /// A tracking number no other test in this class uses. Every test works on its own rows, so tests do not depend
    /// on each other or on execution order even though they share the database.
    /// </summary>
    public string NextTrackingNumber() => $"APX-{20000000 + Interlocked.Increment(ref _next):D8}";

    public async Task InitializeAsync() => Harness = await ServiceHarness.CreateAsync();

    public async Task DisposeAsync() => await Harness.DisposeAsync();
}

/// <summary>
/// Risks R2, R3 and R8 (TESTING-STRATEGY.md §5): the courier-tracking repository and queries against a REAL SQL Server
/// database built from the real migrations. This is what the Moq tests in ApexPost.UnitTests cannot prove:
/// the unique indexes exist, SqlException 2601/2627 is mapped to DuplicateKeyException, the LINQ translates,
/// the cascade and the check constraints are enforced by the database.
/// </summary>
public sealed class TrackingPersistenceTests(TrackingDatabaseFixture fixture) : IClassFixture<TrackingDatabaseFixture>
{
    private static readonly DateTimeOffset ScanTime = new(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);

    private ServiceHarness Harness => fixture.Harness;

    private static ShipmentDetails Details(string city = "Melbourne", decimal kg = 2.5m) =>
        new("Test Recipient", "+61 400 000 111", "1 Test Street", city, kg);

    /// <summary>Creates a shipment through the real service in its own scope (like one HTTP request).</summary>
    private async Task<string> CreateAsync(string? city = null)
    {
        var tn = fixture.NextTrackingNumber();
        using var scope = Harness.NewScope();
        await scope.ServiceProvider.GetRequiredService<IShipmentTrackingService>()
            .CreateAsync(new CreateTrackedShipmentCommand(tn, Details(city ?? "Melbourne")), CancellationToken.None);
        return tn;
    }

    private async Task<ScanUploadResult> UploadAsync(string tn, params ScanInput[] scans)
    {
        using var scope = Harness.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IShipmentTrackingService>().UploadScansAsync(tn, scans, CancellationToken.None);
    }

    private static ScanInput Scan(ScanType type, int minute, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), type, "Melbourne Depot", ScanTime.AddMinutes(minute), null);

    // ---------------------------------------------------------------- unique indexes (R2, R3)

    [Fact]
    public async Task SaveChanges_SecondShipmentWithSameTrackingNumber_ThrowsDuplicateKeyNamingTheUniqueIndex()
    {
        // Arrange: bypass the service's ExistsAsync check on purpose, to prove the DATABASE enforces uniqueness
        var tn = await CreateAsync();
        using var scope = Harness.NewScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITrackedShipmentRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        repository.Add(TrackedShipment.Create(tn, "Rival", "+61 400 000 222", "2 Other Street", "Sydney", 1m, ScanTime));

        // Act
        var ex = await Assert.ThrowsAsync<DuplicateKeyException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.Equal("UQ_TrackedShipments_TrackingNumber", ex.ConstraintName);
        await using var db = Harness.NewContext();
        Assert.Equal(1, await db.TrackedShipments.CountAsync(s => s.TrackingNumber == tn));
    }

    [Fact]
    public async Task SaveChanges_SameScanIdOnTwoShipments_ThrowsDuplicateKeyNamingTheScanIndex()
    {
        // Arrange
        var scanId = Guid.NewGuid();
        var first = await CreateAsync();
        var second = await CreateAsync();
        await UploadAsync(first, Scan(ScanType.PickedUp, 0, scanId));

        using var scope = Harness.NewScope();
        var shipment = (await scope.ServiceProvider.GetRequiredService<ITrackedShipmentRepository>().GetByTrackingNumberAsync(second, CancellationToken.None))!;
        shipment.AddScan(scanId, ScanType.PickedUp, "Sydney Depot", ScanTime, null); // bypasses the service's owner check

        // Act
        var ex = await Assert.ThrowsAsync<DuplicateKeyException>(
            () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.Equal("UQ_ShipmentScans_ScanId", ex.ConstraintName);
    }

    [Fact]
    public async Task CreateAsync_TwoParallelRequestsForSameNumber_ExactlyOneSucceedsAndTheOtherIsAConflict()
    {
        // Arrange: two requests, each with its own scope and DbContext, racing for the same tracking number
        var tn = fixture.NextTrackingNumber();
        async Task<Exception?> Attempt()
        {
            using var scope = Harness.NewScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IShipmentTrackingService>()
                    .CreateAsync(new CreateTrackedShipmentCommand(tn, Details()), CancellationToken.None);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        // Act
        var outcomes = await Task.WhenAll(Attempt(), Attempt());

        // Assert: whichever path the loser took (ExistsAsync saw the row, or the unique index rejected the insert),
        // the answer is the same 409 and the database holds exactly one row.
        Assert.Single(outcomes, o => o is null);
        var conflict = Assert.IsType<ConflictException>(Assert.Single(outcomes, o => o is not null));
        Assert.Equal("DUPLICATE_TRACKING_NUMBER", conflict.ErrorCode);
        await using var db = Harness.NewContext();
        Assert.Equal(1, await db.TrackedShipments.CountAsync(s => s.TrackingNumber == tn));
    }

    [Fact]
    public async Task UploadScansAsync_SameBatchTwice_StoresEachScanOnce()
    {
        // Arrange
        var tn = await CreateAsync();
        ScanInput[] batch = [Scan(ScanType.PickedUp, 0), Scan(ScanType.Arrived, 10)];
        await UploadAsync(tn, batch);

        // Act: the courier app retries after a lost response
        var retry = await UploadAsync(tn, batch);

        // Assert
        Assert.Equal(0, retry.Accepted);
        Assert.Equal(2, retry.DuplicatesSkipped);
        await using var db = Harness.NewContext();
        var shipmentId = await db.TrackedShipments.Where(s => s.TrackingNumber == tn).Select(s => s.Id).SingleAsync();
        Assert.Equal(2, await db.ShipmentScans.CountAsync(s => s.ShipmentId == shipmentId));
    }

    // ---------------------------------------------------------------- repository queries

    [Fact]
    public async Task ExistsAsync_SavedAndUnknownNumbers_ReturnsTrueOnlyForTheSavedOne()
    {
        // Arrange
        var tn = await CreateAsync();
        using var scope = Harness.NewScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITrackedShipmentRepository>();

        // Act
        var saved = await repository.ExistsAsync(tn, CancellationToken.None);
        var unknown = await repository.ExistsAsync("APX-29999999", CancellationToken.None);

        // Assert
        Assert.True(saved);
        Assert.False(unknown);
    }

    [Fact]
    public async Task GetScanOwnersAsync_MixOfStoredAndNewIds_ReturnsOwnerIdOnlyForStoredOnes()
    {
        // Arrange
        var stored = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        var tn = await CreateAsync();
        await UploadAsync(tn, Scan(ScanType.PickedUp, 0, stored));
        await using var db = Harness.NewContext();
        var shipmentId = await db.TrackedShipments.Where(s => s.TrackingNumber == tn).Select(s => s.Id).SingleAsync();
        using var scope = Harness.NewScope();

        // Act
        var owners = await scope.ServiceProvider.GetRequiredService<ITrackedShipmentRepository>()
            .GetScanOwnersAsync([stored, fresh], CancellationToken.None);

        // Assert
        var owner = Assert.Single(owners);
        Assert.Equal(stored, owner.Key);
        Assert.Equal(shipmentId, owner.Value);
    }

    [Fact]
    public async Task ListAsync_FilterByStatusAndCity_ReturnsMatchingRowsNewestFirstWithCorrectPaging()
    {
        // Arrange: a city no other test uses, three shipments created at different times, one moved on
        const string city = "Ballarat-ListTest";
        Harness.Clock.Now = ScanTime;
        var oldest = await CreateAsync(city);
        Harness.Clock.Now = ScanTime.AddMinutes(1);
        var middle = await CreateAsync(city);
        Harness.Clock.Now = ScanTime.AddMinutes(2);
        var newest = await CreateAsync(city);
        using (var scope = Harness.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IShipmentTrackingService>()
                .ChangeStatusAsync(middle, TrackingStatus.PickedUp, CancellationToken.None);
        }

        using var queryScope = Harness.NewScope();
        var queries = queryScope.ServiceProvider.GetRequiredService<ITrackingQueries>();

        // Act
        var created = await queries.ListAsync(new TrackingListQuery(1, 10, TrackingStatus.Created, city), CancellationToken.None);
        var page2 = await queries.ListAsync(new TrackingListQuery(2, 1, null, city), CancellationToken.None);

        // Assert
        Assert.Equal([newest, oldest], created.Items.Select(i => i.TrackingNumber));
        Assert.Equal(2, created.TotalCount);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(3, page2.TotalPages);
        Assert.Equal(middle, Assert.Single(page2.Items).TrackingNumber);
    }

    [Fact]
    public async Task GetAsync_ShipmentWithScans_ProjectsTheLatestScanByScanTime()
    {
        // Arrange: upload out of order; the latest by ScannedAt must win, not the last inserted
        var tn = await CreateAsync();
        await UploadAsync(tn, Scan(ScanType.Arrived, 30), Scan(ScanType.PickedUp, 0));
        using var scope = Harness.NewScope();

        // Act
        var dto = await scope.ServiceProvider.GetRequiredService<ITrackingQueries>().GetAsync(tn, CancellationToken.None);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(TrackingStatus.InTransit, dto.Status);
        Assert.Equal(ScanType.Arrived, dto.LatestScan!.ScanType);
        Assert.Equal(ScanTime.AddMinutes(30), dto.LatestScan.ScannedAt);
    }

    [Fact]
    public async Task GetScansAsync_KnownAndUnknownShipment_ReturnsOldestFirstOrNull()
    {
        // Arrange
        var tn = await CreateAsync();
        await UploadAsync(tn, Scan(ScanType.Departed, 20), Scan(ScanType.PickedUp, 0), Scan(ScanType.Arrived, 10));
        using var scope = Harness.NewScope();
        var queries = scope.ServiceProvider.GetRequiredService<ITrackingQueries>();

        // Act
        var scans = await queries.GetScansAsync(tn, CancellationToken.None);
        var unknown = await queries.GetScansAsync("APX-29999998", CancellationToken.None);

        // Assert
        Assert.Equal([ScanType.PickedUp, ScanType.Arrived, ScanType.Departed], scans!.Select(s => s.ScanType));
        Assert.Null(unknown);
    }

    // ---------------------------------------------------------------- database rules

    [Fact]
    public async Task Remove_ShipmentWithScans_CascadesToItsScans()
    {
        // Arrange
        var tn = await CreateAsync();
        await UploadAsync(tn, Scan(ScanType.PickedUp, 0), Scan(ScanType.Arrived, 10));
        await using var before = Harness.NewContext();
        var shipmentId = await before.TrackedShipments.Where(s => s.TrackingNumber == tn).Select(s => s.Id).SingleAsync();

        // Act: the repository loads WITHOUT scans, so only the database's ON DELETE CASCADE can remove them
        using (var scope = Harness.NewScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITrackedShipmentRepository>();
            repository.Remove((await repository.GetByTrackingNumberAsync(tn, CancellationToken.None))!);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        // Assert
        await using var after = Harness.NewContext();
        Assert.False(await after.TrackedShipments.AnyAsync(s => s.Id == shipmentId));
        Assert.Equal(0, await after.ShipmentScans.CountAsync(s => s.ShipmentId == shipmentId));
    }

    [Theory]
    [InlineData("70.001")]
    [InlineData("0.099")]
    public async Task CheckConstraint_WeightOutsideRangeWrittenBySql_IsRejectedByTheDatabase(string weight)
    {
        // Arrange: bypass the domain and the service with raw SQL, as a buggy script or another app could.
        // The parameter is typed decimal(9,3) like the column on purpose: an untyped decimal parameter is sent as
        // decimal(18,2), which silently rounds 70.001 to 70.00 (valid!), so the test would pass the boundary without
        // ever testing it. Found while writing this test; see TESTING-STRATEGY.md reflection.
        var tn = await CreateAsync();
        await using var db = Harness.NewContext();
        var kg = new SqlParameter("@kg", System.Data.SqlDbType.Decimal)
        {
            Precision = 9,
            Scale = 3,
            Value = decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture),
        };
        var number = new SqlParameter("@tn", System.Data.SqlDbType.VarChar, 20) { Value = tn };

        // Act
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE TrackedShipments SET WeightKg = @kg WHERE TrackingNumber = @tn", kg, number));

        // Assert: 547 = constraint violation, and it is the weight range constraint
        Assert.Equal(547, ex.Number);
        Assert.Contains("CK_TrackedShipments_WeightKg_Range", ex.Message, StringComparison.Ordinal);
    }
}
