# ApexPost.Api notes (Week 4, Parts B and C)

REST API for courier shipment tracking. Controllers are thin (bind, call `IShipmentTrackingService`,
return a result); every rule lives in `ApexPost.Services/Tracking`. It runs on the Week 3 stack:
EF Core 10, SQL Server `ApexPostDb`, the same DI extension methods as the MVC app.

## Endpoints

Base path `/api/v2/shipments` (current) and `/api/v1/shipments` (deprecated, same routes, flat responses with a numeric status; see [VERSIONING.md](VERSIONING.md)). The tables below show the v2 paths; replace `v2` with `v1` for the deprecated version. The unversioned `/api/v2/shipments` no longer exists (404). `{trackingNumber}` must match `^APX-\d{8}$` (route constraint; anything else is a 404).
Request bodies must be `application/json` (otherwise 415). Every error is `application/problem+json`.

| Verb | Route | Route name (v2; v1 adds a `V1` suffix) | Purpose | Success | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v2/shipments?page=&pageSize=&status=&city=` | `ListShipments` | Paged, filtered list (newest first). `pageSize` default 20, capped at 50 | 200 | 400 (unknown `status`), 500 |
| GET | `/api/v2/shipments/{trackingNumber}` | `GetShipment` | One shipment | 200 | 404, 500 |
| POST | `/api/v2/shipments` | `CreateShipment` | Create a shipment in status `Created` | 201 + `Location` | 400, 409 (duplicate tracking number), 415, 500 |
| PUT | `/api/v2/shipments/{trackingNumber}` | `UpdateShipment` | Replace editable details (not the tracking number). Only while `Created` | 200 | 400, 404, 409 (already picked up), 415, 500 |
| PATCH | `/api/v2/shipments/{trackingNumber}/status` | `UpdateShipmentStatus` | Move to the next status | 200 | 400, 404, 409 (illegal transition), 415, 500 |
| DELETE | `/api/v2/shipments/{trackingNumber}` | `DeleteShipment` | Delete. Only while `Created` | 204 | 404, 409 (not `Created`), 500 |
| POST | `/api/v2/shipments/{trackingNumber}/scans` | `UploadScans` | Bulk upload of offline scans (idempotent) | 200 | 400, 404, 415, 500 |
| GET | `/api/v2/shipments/{trackingNumber}/scans` | `GetScans` | Scan history, oldest first | 200 | 404, 500 |

## Status codes

| Code | When |
| --- | --- |
| 200 | Successful read, update, status change, or scan upload (the body says what was stored) |
| 201 | Shipment created; `Location` points at `GET /api/shipments/{trackingNumber}` (`CreatedAtAction`) |
| 204 | Shipment deleted |
| 400 | Validation failed: `ValidationProblemDetails` with an `errors` object per field |
| 404 | Unknown tracking number (or a tracking number that does not match `APX-` + 8 digits) |
| 409 | Duplicate tracking number, illegal status transition, editing or deleting a shipment that is no longer `Created`. The body has an `errorCode` (`DUPLICATE_TRACKING_NUMBER`, `INVALID_STATUS_TRANSITION`, `SHIPMENT_LOCKED`, `SHIPMENT_NOT_DELETABLE`, `CONCURRENCY_CONFLICT`, `DUPLICATE_KEY`) |
| 415 | Body is not `application/json` (`[Consumes]`) |
| 500 | Unhandled error, written by `ApiExceptionHandler`. Stack traces and exception text are only written in Development; the response always carries `traceId` and `correlationId` |

## Rules (all in `ShipmentTrackingService` / `TrackedShipment`)

* Status machine: `Created -> PickedUp -> InTransit -> OutForDelivery -> Delivered | Failed`. Anything else is a 409. `Delivered` and `Failed` are final.
* Delete and PUT are allowed only while the status is `Created`.
* A duplicate tracking number is a 409, including when two requests race (the unique index `UQ_TrackedShipments_TrackingNumber` decides).
* `pageSize` above 50 is reduced to 50; zero or negative becomes 20; `page` below 1 becomes 1.
* Validation (DTO attributes): tracking number `^APX-\d{8}$`; weight 0.1 to 70 kg; recipient name, phone, address, city required; phone `+`/digits/spaces/dashes/brackets, 8 to 20 characters.
* Custom rules (`IValidatableObject` on `ScanItemRequest`): a scan time cannot be in the future (one minute of clock drift tolerated); a `Delivered` scan needs `recipientSignatureName`.

## Idempotent scan upload

`POST /api/shipments/{trackingNumber}/scans` takes 1 to 100 scans, each with the Part A `scanId` (a GUID made on the device).

```json
{ "scans": [ { "scanId": "3f2b8c1e-6a4d-4e0b-9a51-7d1c2e8f4b10", "scanType": "PickedUp", "location": "Melbourne Depot", "scannedAtUtc": "2026-11-08T22:00:00Z" } ] }
```

Response: `{ "accepted": 1, "duplicatesSkipped": 0, "rejected": [ { "scanId": "...", "reason": "..." } ] }`.

* `ScanId` is a UNIQUE index (`UQ_ShipmentScans_ScanId`). Sending the same scan again returns `accepted: 0, duplicatesSkipped: 1`, so a retry after a dropped connection is safe.
* The service looks the ids up first. If another request stores one of them between that check and the insert, the unique index raises a duplicate-key error; the service then re-inserts the batch one scan at a time and counts the losers as `duplicatesSkipped`.
* `rejected` lists scans that cannot be stored: the scan id belongs to a different shipment, or the shipment is already `Delivered`/`Failed`.
* Scans are applied in scan-time order. A scan moves the status forward only when the move is legal (`PickedUp` scan: `Created -> PickedUp`; `Arrived`/`Departed`: `PickedUp -> InTransit`; `OutForDelivery`; `Delivered`; `DeliveryFailed`: `OutForDelivery -> Failed`). Otherwise the scan is stored and the status is left alone, so an out-of-order upload never moves a shipment backwards.
* JSON enums travel as names (`"status": "InTransit"`); numbers are still accepted on input. The one exception is the v1 shipment `status`, which is a number (see VERSIONING.md).

## Data

Additive: two new tables, `TrackedShipments` and `ShipmentScans`, created by migration `AddCourierTracking`. The Week 3 tables, entities and services are untouched. The courier API's `TrackedShipment` is separate from the counter-sale `Shipment`, so its tracking numbers (`APX-` + 8 digits) are independent of the Week 3 ones.

## Run it (PowerShell, from the repo root)

```powershell
dotnet user-secrets set "ConnectionStrings:ApexPostDb" "Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True" --project .\ApexPost.Api
$env:APEXPOST_DESIGN_CONNECTION = "Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update --project .\ApexPost.Database
dotnet run --project .\ApexPost.Api --launch-profile https      # https://localhost:7153
```

Then open `ApexPost.Api/ApexPost.Api.http`. Tests (no database needed): `dotnet test .\ApexPost.Api.Tests`.


## OpenAPI and Swagger UI (Part C)

* Documents: `/openapi/v1.json`, `/openapi/v2.json` (built-in ASP.NET Core generator, OpenAPI 3.0). Swagger UI: `/swagger` with a version dropdown ("v2", "v1 (deprecated)").
  All three are **Development only**.
* The documents carry title "ApexPost Logistics API", version, a fictional contact (`api-team@apexpost.example`),
  summaries from `[EndpointSummary]`, descriptions and response codes from the XML comments and `[ProducesResponseType]`, and tags (`Shipments`, `Scans`).
* Committed contracts: `contracts/v1.json`, `contracts/v2.json` (`scripts/export-contracts.ps1`).
* Client SDK: `ApexPost.Client` (NSwag, from `contracts/v2.json`, `scripts/generate-client.ps1`) and the console consumer `ApexPost.Client.Consumer`.
