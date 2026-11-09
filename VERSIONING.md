# API versioning (Week 4, Part C)

ApexPost.Api is versioned in the URL: `/api/v1/...` and `/api/v2/...`. The version is part of the address, so
it shows up in logs, in the `.http` file, in Swagger UI and in every client, and a cached or bookmarked URL
can never silently change meaning.

## Why URL-segment versioning

| Option | Verdict |
| --- | --- |
| **URL segment** (`/api/v2/shipments`) | **Chosen.** Visible, easy to route, easy to test in a browser, and the generated SDK and the OpenAPI document both carry the version in every path |
| Query string (`?api-version=2`) | Easy to forget; caches and proxies treat the URLs as one resource |
| Header (`X-Api-Version: 2`) | Invisible in links and screenshots; clients forget it and silently get the default |
| Media type (`application/vnd.apexpost.v2+json`) | Most "RESTful" but hard for couriers' apps and tooling; error-prone to debug |

Library: `Asp.Versioning.Mvc` 8.1 with `UrlSegmentApiVersionReader`. There is no default version for an
unversioned URL (`AssumeDefaultVersionWhenUnspecified = false`): `/api/shipments` is a 404, never a guess.

## Current versions

| Version | Status | Routes | Notes |
| --- | --- | --- | --- |
| **v2** (2.0) | Current | `/api/v2/shipments...` | The contract the SDK and the MAUI app use |
| **v1** (1.0) | **Deprecated** | `/api/v1/shipments...` | Works, frozen. Every operation is flagged `deprecated` in its OpenAPI document and Swagger UI shows it struck through |

Every response carries `api-supported-versions` (`1.0, 2.0`) and, while v1 is deprecated,
`api-deprecated-versions` (`1.0`) (`ReportApiVersions = true`). A client can detect it is on a deprecated version
without reading any documentation.

## What is breaking in v2

The scan endpoints (`POST` / `GET .../scans`) are identical in both versions and are served by one controller.
Only the shipment representation changed, which is why v2 is a new version and not a quiet change to v1.

| | v1 (deprecated) | v2 |
| --- | --- | --- |
| Recipient | flat: `recipientName`, `recipientPhone`, `address`, `city` | nested: `recipient: { name, phone, address, city }` |
| Status | **number**: `0` Created, `1` PickedUp, `2` InTransit, `3` OutForDelivery, `4` Delivered, `5` Failed | **string**: `"InTransit"` |
| Where is it? | not included | `latestScan: { scanType, location, scannedAtUtc }` (null until the first scan) |
| Request bodies | unchanged | unchanged (flat); only responses changed |

Example, the same shipment:

```jsonc
// GET /api/v1/shipments/APX-10000001
{ "trackingNumber": "APX-10000001", "recipientName": "Ada Lovelace", "recipientPhone": "+61 400 000 111",
  "address": "12 Example Street", "city": "Melbourne", "weightKg": 2.5, "status": 1, "createdAtUtc": "2026-11-08T22:00:00+00:00" }

// GET /api/v2/shipments/APX-10000001
{ "trackingNumber": "APX-10000001",
  "recipient": { "name": "Ada Lovelace", "phone": "+61 400 000 111", "address": "12 Example Street", "city": "Melbourne" },
  "weightKg": 2.5, "status": "PickedUp", "createdAtUtc": "2026-11-08T22:00:00+00:00",
  "latestScan": { "scanType": "PickedUp", "location": "Melbourne Depot", "scannedAtUtc": "2026-11-08T22:05:00+00:00" } }
```

Both versions read the same data through the same `IShipmentTrackingService`. The versions differ only in
the DTOs and the mapping in `ApiMapper` (`ToV1`, `ToV2`); no business rule is duplicated.

Request status values (`PATCH .../status`, `?status=` filter) accept names in both versions, and numbers are still accepted on input.

## Migration guide: v1 to v2

1. Change the base path from `/api/v1/` to `/api/v2/`. Nothing else about requests changes.
2. Read `recipient.name`, `recipient.phone`, `recipient.address` and `recipient.city` instead of the flat fields.
3. Compare `status` as a string (`"Delivered"`), not a number.
4. Use `latestScan` instead of a second call to `GET .../scans` when you only need the last known position.
5. Regenerate your client from `contracts/v2.json`.

## Deprecation and retirement policy

* A deprecated version keeps working and receives security fixes only. No new fields, no new endpoints.
* Deprecation is announced in four places: the `api-deprecated-versions` header, `deprecated: true` on every
  v1 operation, the "v1 (deprecated)" entry in Swagger UI, and this document.
* A deprecated version is retired no sooner than 6 months after it is announced, and only after the retirement date
  is added to this file and sent to the API consumers (the courier app team first).
* A new version is created only for a **breaking** change: removing or renaming a field, changing a type
  (number to string), changing the meaning of a status code, or making an optional input mandatory.
  Adding an optional response field, a new endpoint or a new optional query parameter is not breaking and goes into the current version.

## How versions are published

| Document | URL (Development) | Committed copy |
| --- | --- | --- |
| v1 | `/openapi/v1.json` | `contracts/v1.json` |
| v2 | `/openapi/v2.json` | `contracts/v2.json` |

`scripts\export-contracts.ps1` regenerates the committed copies from the running API. A change to the API
that is not reflected in `contracts/` is a review comment.

## Implementation notes

* Controllers: `Controllers/V1/ShipmentsV1Controller` (`[ApiVersion("1.0", Deprecated = true)]`),
  `Controllers/V2/ShipmentsController` (`[ApiVersion("2.0")]`), and `Controllers/ScansController` (both versions).
* Route names must be unique across the app. v2 keeps the clean names (`GetShipment`, `CreateShipment`, ...), which
  become the SDK method names; v1 names carry a `V1` suffix (`GetShipmentV1`).
* DTOs: `Contracts/V1` and `Contracts/V2` hold the version-specific shipment responses; `Contracts/Responses.cs` and
  `Contracts/Requests.cs` hold what both versions share.
