using System.ComponentModel.DataAnnotations;
using ApexPost.Api.Contracts;
using ApexPost.Models.Domain;

namespace ApexPost.Api.Tests;

/// <summary>The DataAnnotations and IValidatableObject rules that [ApiController] turns into 400 ValidationProblemDetails.</summary>
public sealed class RequestValidationTests
{
    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }

    private static bool Complains(IEnumerable<ValidationResult> results, string member) =>
        results.Any(r => r.MemberNames.Contains(member));

    private static CreateShipmentRequest ValidCreate() => new()
    {
        TrackingNumber = "APX-10000001",
        RecipientName = "Ada Lovelace",
        RecipientPhone = "+61 400 000 111",
        Address = "12 Example Street",
        City = "Melbourne",
        WeightKg = 2.5m,
    };

    private static ScanItemRequest ValidScan() => new()
    {
        ScanId = Guid.NewGuid(),
        ScanType = ScanType.PickedUp,
        Location = "Melbourne Depot",
        ScannedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
    };

    [Fact]
    public void CreateShipment_ValidRequest_HasNoErrors() => Assert.Empty(Validate(ValidCreate()));

    [Theory]
    [InlineData("")]
    [InlineData("APX-1234567")]
    [InlineData("APX-123456789")]
    [InlineData("apx-12345678")]
    [InlineData("APX12345678")]
    [InlineData("XYZ-12345678")]
    public void CreateShipment_BadTrackingNumber_IsInvalid(string trackingNumber)
    {
        var request = ValidCreate();
        request.TrackingNumber = trackingNumber;

        Assert.True(Complains(Validate(request), nameof(CreateShipmentRequest.TrackingNumber)));
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.0)]
    [InlineData(70.1)]
    [InlineData(500.0)]
    public void CreateShipment_WeightOutsideRange_IsInvalid(double weight)
    {
        var request = ValidCreate();
        request.WeightKg = (decimal)weight;

        Assert.True(Complains(Validate(request), nameof(CreateShipmentRequest.WeightKg)));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(70.0)]
    public void CreateShipment_WeightAtTheLimits_IsValid(double weight)
    {
        var request = ValidCreate();
        request.WeightKg = (decimal)weight;

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void CreateShipment_EmptyBody_ReportsEveryRequiredField()
    {
        var results = Validate(new CreateShipmentRequest());

        Assert.True(Complains(results, nameof(CreateShipmentRequest.TrackingNumber)));
        Assert.True(Complains(results, nameof(CreateShipmentRequest.RecipientName)));
        Assert.True(Complains(results, nameof(CreateShipmentRequest.RecipientPhone)));
        Assert.True(Complains(results, nameof(CreateShipmentRequest.Address)));
        Assert.True(Complains(results, nameof(CreateShipmentRequest.City)));
        Assert.True(Complains(results, nameof(CreateShipmentRequest.WeightKg)));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("+61 (400) 000 111 ext 5")]
    [InlineData("0400 000 11x")]
    public void CreateShipment_MalformedPhone_IsInvalid(string phone)
    {
        var request = ValidCreate();
        request.RecipientPhone = phone;

        Assert.True(Complains(Validate(request), nameof(CreateShipmentRequest.RecipientPhone)));
    }

    [Theory]
    [InlineData("+61 400 000 111")]
    [InlineData("0390001234")]
    [InlineData("03 9000 1234")]
    [InlineData("+1-555-010-0199")]
    public void CreateShipment_CommonPhoneFormats_AreValid(string phone)
    {
        var request = ValidCreate();
        request.RecipientPhone = phone;

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void UpdateShipment_EmptyBody_ReportsEveryRequiredField()
    {
        var results = Validate(new UpdateShipmentRequest());

        Assert.True(Complains(results, nameof(UpdateShipmentRequest.RecipientName)));
        Assert.True(Complains(results, nameof(UpdateShipmentRequest.RecipientPhone)));
        Assert.True(Complains(results, nameof(UpdateShipmentRequest.Address)));
        Assert.True(Complains(results, nameof(UpdateShipmentRequest.City)));
        Assert.True(Complains(results, nameof(UpdateShipmentRequest.WeightKg)));
    }

    [Fact]
    public void UpdateStatus_MissingStatus_IsInvalid() =>
        Assert.True(Complains(Validate(new UpdateStatusRequest()), nameof(UpdateStatusRequest.Status)));

    [Fact]
    public void UpdateStatus_KnownStatus_IsValid() =>
        Assert.Empty(Validate(new UpdateStatusRequest { Status = TrackingStatus.PickedUp }));

    // ---- the custom IValidatableObject rules ----

    [Fact]
    public void Scan_ValidItem_HasNoErrors() => Assert.Empty(Validate(ValidScan()));

    [Fact]
    public void Scan_InTheFuture_IsInvalid()
    {
        var scan = ValidScan();
        scan.ScannedAtUtc = DateTimeOffset.UtcNow.AddDays(1);

        var results = Validate(scan);

        Assert.True(Complains(results, nameof(ScanItemRequest.ScannedAtUtc)));
        Assert.Contains(results, r => r.ErrorMessage!.Contains("future", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_SecondsAheadOfTheServerClock_IsToleratedAsClockDrift()
    {
        var scan = ValidScan();
        scan.ScannedAtUtc = DateTimeOffset.UtcNow.AddSeconds(20);

        Assert.Empty(Validate(scan));
    }

    [Fact]
    public void Scan_DeliveredWithoutSignature_IsInvalid()
    {
        var scan = ValidScan();
        scan.ScanType = ScanType.Delivered;
        scan.RecipientSignatureName = "  ";

        Assert.True(Complains(Validate(scan), nameof(ScanItemRequest.RecipientSignatureName)));
    }

    [Fact]
    public void Scan_DeliveredWithSignature_IsValid()
    {
        var scan = ValidScan();
        scan.ScanType = ScanType.Delivered;
        scan.RecipientSignatureName = "A. Lovelace";

        Assert.Empty(Validate(scan));
    }

    [Fact]
    public void Scan_NonDeliveredWithoutSignature_IsValid()
    {
        var scan = ValidScan();
        scan.ScanType = ScanType.Arrived;

        Assert.Empty(Validate(scan));
    }

    [Fact]
    public void Scan_EmptyGuid_IsInvalid()
    {
        var scan = ValidScan();
        scan.ScanId = Guid.Empty;

        Assert.True(Complains(Validate(scan), nameof(ScanItemRequest.ScanId)));
    }

    [Fact]
    public void Scan_MissingTypeAndTime_IsInvalid()
    {
        var results = Validate(new ScanItemRequest { ScanId = Guid.NewGuid(), Location = "Depot" });

        Assert.True(Complains(results, nameof(ScanItemRequest.ScanType)));
        Assert.True(Complains(results, nameof(ScanItemRequest.ScannedAtUtc)));
    }

    [Fact]
    public void ScanUpload_EmptyList_IsInvalid() =>
        Assert.True(Complains(Validate(new ScanUploadRequest()), nameof(ScanUploadRequest.Scans)));

    [Fact]
    public void ScanUpload_MoreThanOneHundredScans_IsInvalid()
    {
        var request = new ScanUploadRequest { Scans = Enumerable.Range(0, 101).Select(_ => ValidScan()).ToList() };

        Assert.True(Complains(Validate(request), nameof(ScanUploadRequest.Scans)));
    }

    [Fact]
    public void ScanUpload_OneToOneHundredScans_IsValid()
    {
        Assert.Empty(Validate(new ScanUploadRequest { Scans = [ValidScan()] }));
        Assert.Empty(Validate(new ScanUploadRequest { Scans = Enumerable.Range(0, 100).Select(_ => ValidScan()).ToList() }));
    }
}
