using System.Text.RegularExpressions;
using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Services.Abstractions;

namespace ApexPost.Services.Tracking;

/// <summary>
/// Courier tracking use cases. Controllers call this and nothing else: duplicate detection, the
/// status machine, the Created-only delete rule, paging limits and scan idempotency are all here.
/// </summary>
public sealed partial class ShipmentTrackingService(
    ITrackedShipmentRepository shipments,
    ITrackingQueries queries,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IShipmentTrackingService
{
    public Task<PagedResult<TrackedShipmentDto>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var clamped = query with
        {
            Page = Math.Max(1, query.Page),
            PageSize = query.PageSize <= 0 ? TrackingLimits.DefaultPageSize : Math.Min(query.PageSize, TrackingLimits.MaxPageSize),
            City = string.IsNullOrWhiteSpace(query.City) ? null : query.City.Trim()
        };
        return queries.ListAsync(clamped, cancellationToken);
    }

    public async Task<TrackedShipmentDto> GetAsync(string trackingNumber, CancellationToken cancellationToken) =>
        await queries.GetAsync(trackingNumber, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException("Shipment", trackingNumber);

    public async Task<TrackedShipmentDto> CreateAsync(CreateTrackedShipmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = ValidateDetails(command.Details);
        if (!TrackingNumberRegex().IsMatch(command.TrackingNumber ?? string.Empty))
        {
            errors.Add(new ValidationError(nameof(command.TrackingNumber), "Tracking number must look like APX-12345678."));
        }

        if (errors.Count > 0)
        {
            throw new CommandValidationException(errors);
        }

        if (await shipments.ExistsAsync(command.TrackingNumber!, cancellationToken).ConfigureAwait(false))
        {
            throw DuplicateTrackingNumber(command.TrackingNumber!);
        }

        var d = command.Details;
        var shipment = TrackedShipment.Create(command.TrackingNumber!, d.RecipientName, d.RecipientPhone, d.AddressLine, d.City, d.WeightKg, clock.GetUtcNow());
        shipments.Add(shipment);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            // Two creates raced past the ExistsAsync check; the unique index decided.
            unitOfWork.DiscardChanges();
            throw DuplicateTrackingNumber(command.TrackingNumber!);
        }

        return await GetAsync(command.TrackingNumber!, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackedShipmentDto> UpdateAsync(string trackingNumber, ShipmentDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var errors = ValidateDetails(details);
        if (errors.Count > 0)
        {
            throw new CommandValidationException(errors);
        }

        var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        shipment.Update(details.RecipientName, details.RecipientPhone, details.AddressLine, details.City, details.WeightKg);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackedShipmentDto> ChangeStatusAsync(string trackingNumber, TrackingStatus status, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(status))
        {
            throw new CommandValidationException(nameof(status), "Status is not a valid value.");
        }

        var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        shipment.ChangeStatus(status);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string trackingNumber, CancellationToken cancellationToken)
    {
        var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        shipment.EnsureDeletable();
        shipments.Remove(shipment);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScanDto>> GetScansAsync(string trackingNumber, CancellationToken cancellationToken) =>
        await queries.GetScansAsync(trackingNumber, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException("Shipment", trackingNumber);

    public async Task<ScanUploadResult> UploadScansAsync(string trackingNumber, IReadOnlyList<ScanInput> scans, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scans);
        if (scans.Count is 0 or > TrackingLimits.MaxScansPerUpload)
        {
            throw new CommandValidationException(nameof(scans), $"Upload between 1 and {TrackingLimits.MaxScansPerUpload} scans per request.");
        }

        var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        var owners = await shipments.GetScanOwnersAsync(scans.Select(s => s.ScanId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);

        var duplicates = 0;
        var rejected = new List<RejectedScan>();
        var candidates = new List<ScanInput>();
        var seenInBatch = new HashSet<Guid>();

        foreach (var scan in scans)
        {
            if (scan.ScanId == Guid.Empty)
            {
                rejected.Add(new RejectedScan(scan.ScanId, "ScanId is required."));
            }
            else if (!seenInBatch.Add(scan.ScanId))
            {
                duplicates++; // repeated inside the same request
            }
            else if (owners.TryGetValue(scan.ScanId, out var ownerId))
            {
                if (ownerId == shipment.Id)
                {
                    duplicates++;
                }
                else
                {
                    rejected.Add(new RejectedScan(scan.ScanId, "ScanId is already recorded for a different shipment."));
                }
            }
            else if (Rejection(shipment, scan) is { } reason)
            {
                rejected.Add(new RejectedScan(scan.ScanId, reason));
            }
            else
            {
                candidates.Add(scan);
            }
        }

        var ordered = candidates.OrderBy(s => s.ScannedAt).ToList();
        var accepted = 0;
        if (ordered.Count > 0)
        {
            foreach (var scan in ordered)
            {
                shipment.AddScan(scan.ScanId, scan.ScanType, scan.Location, scan.ScannedAt, scan.RecipientSignatureName);
                shipment.AdvanceFor(scan.ScanType);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                accepted = ordered.Count;
            }
            catch (DuplicateKeyException)
            {
                // Another request stored one of these ScanIds between our check and our insert (the
                // unique index on ScanId won). Redo the batch one scan at a time and count the losers.
                var retry = await InsertOneByOneAsync(trackingNumber, ordered, cancellationToken).ConfigureAwait(false);
                accepted = retry.Accepted;
                duplicates += retry.Duplicates;
            }
        }

        return new ScanUploadResult(accepted, duplicates, rejected);
    }

    private async Task<(int Accepted, int Duplicates)> InsertOneByOneAsync(string trackingNumber, IReadOnlyList<ScanInput> scans, CancellationToken cancellationToken)
    {
        var accepted = 0;
        var duplicates = 0;
        foreach (var scan in scans)
        {
            unitOfWork.DiscardChanges();
            var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
            shipment.AddScan(scan.ScanId, scan.ScanType, scan.Location, scan.ScannedAt, scan.RecipientSignatureName);
            shipment.AdvanceFor(scan.ScanType);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                accepted++;
            }
            catch (DuplicateKeyException)
            {
                unitOfWork.DiscardChanges();
                duplicates++;
            }
        }

        return (accepted, duplicates);
    }

    private static string? Rejection(TrackedShipment shipment, ScanInput scan)
    {
        if (!Enum.IsDefined(scan.ScanType))
        {
            return "ScanType is not a valid value.";
        }

        if (string.IsNullOrWhiteSpace(scan.Location) || scan.Location.Trim().Length > 100)
        {
            return "Location is required and must be 100 characters or fewer.";
        }

        if (scan.ScanType == ScanType.Delivered && string.IsNullOrWhiteSpace(scan.RecipientSignatureName))
        {
            return "A Delivered scan requires the recipient's signature name.";
        }

        if (!string.IsNullOrWhiteSpace(scan.RecipientSignatureName) && scan.RecipientSignatureName.Trim().Length > 100)
        {
            return "RecipientSignatureName must be 100 characters or fewer.";
        }

        if (shipment.Status is TrackingStatus.Delivered or TrackingStatus.Failed)
        {
            return $"The shipment is already {shipment.Status}; no further scans are accepted.";
        }

        return null;
    }

    private async Task<TrackedShipment> LoadAsync(string trackingNumber, CancellationToken cancellationToken) =>
        await shipments.GetByTrackingNumberAsync(trackingNumber, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException("Shipment", trackingNumber);

    private static ConflictException DuplicateTrackingNumber(string trackingNumber) =>
        new("DUPLICATE_TRACKING_NUMBER", $"A shipment with tracking number {trackingNumber} already exists.");

    private static List<ValidationError> ValidateDetails(ShipmentDetails details)
    {
        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(details.RecipientName))
        {
            errors.Add(new ValidationError(nameof(details.RecipientName), "Recipient name is required."));
        }

        if (string.IsNullOrWhiteSpace(details.RecipientPhone) || !PhoneRegex().IsMatch(details.RecipientPhone.Trim()))
        {
            errors.Add(new ValidationError(nameof(details.RecipientPhone), "A valid recipient phone number is required."));
        }

        if (string.IsNullOrWhiteSpace(details.AddressLine))
        {
            errors.Add(new ValidationError(nameof(details.AddressLine), "Address is required."));
        }

        if (string.IsNullOrWhiteSpace(details.City))
        {
            errors.Add(new ValidationError(nameof(details.City), "City is required."));
        }

        if (details.WeightKg is < 0.1m or > 70m)
        {
            errors.Add(new ValidationError(nameof(details.WeightKg), "Weight must be between 0.1 and 70 kg."));
        }

        return errors;
    }

    [GeneratedRegex(TrackingLimits.TrackingNumberPattern, RegexOptions.CultureInvariant)]
    private static partial Regex TrackingNumberRegex();

    [GeneratedRegex(TrackingLimits.PhonePattern, RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();
}
