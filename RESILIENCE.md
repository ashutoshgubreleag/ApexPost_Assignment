# Courier app resilience (Week 4, Part D)

How the MAUI courier app (`ApexPost.Courier`, logic in `ApexPost.Courier.Core`) behaves when the network or the server misbehaves.
The rule behind everything: **saving a scan never depends on the network.** The scan is written to the local Couchbase Lite
database first (Part A); uploading is a separate, retried, rate-limited background job.

## The pipeline

`IHttpClientFactory` gives one typed client (`ApexApiClient`). Handlers, outermost first:

| # | Handler | Job |
| --- | --- | --- |
| 1 | `AuthHeaderHandler` | Adds `Authorization: Bearer <jwt>` from `SecureStorage`. Outside the retry so every retry reuses the header. A 401 clears the token. |
| 2 | Total timeout (40 s) | Upper bound for one upload including every retry. |
| 3 | Retry | Up to 3 retries, exponential backoff (0.5 s, 1 s, 2 s) **with jitter**. |
| 4 | Circuit breaker | Opens at ≥ 50 % failures over 30 s (at least 4 calls). Open for 30 s, then one trial call (half-open). |
| 5 | Attempt timeout (10 s) | One slow attempt cannot hold the app. |

Implemented in `ApexPost.Courier.Core/Resilience/ResilienceExtensions.cs` (Polly v8 through `Microsoft.Extensions.Http.Resilience`);
every number is a setting in `ResilienceSettings`.

## What is retried, and why only that

| Outcome | Treated as | Retried? |
| --- | --- | --- |
| No connection, DNS failure, connection reset (`HttpRequestException`) | transient | yes |
| Attempt timeout | transient | yes |
| 408, 500, 502, 503, 504 | transient | yes |
| 400, 404, 409, 403 | the server's real answer | **no**: the scans are marked *Failed* with the Problem Details text |
| 401 | session expired | **no**: token cleared, scans stay Pending, user gets a new token on the sign-in page |
| Caller cancelled | not a failure | no, and scans stay Pending |

Repeating a 400 would give the same 400 every time and only add load, so the pipeline does not do it.
**POST is retried** because it is safe here: every scan carries a client-generated `scanId` and the API de-duplicates on it
(Part B), so a request that succeeded but whose response was lost can be sent again without creating a second scan.
A duplicate answer counts as success.

## Backoff with jitter

Delays double each retry. Jitter adds randomness so that many phones that lost the network at the same moment (a depot Wi-Fi outage)
do not all retry in the same instant and knock the recovering server over again (the "thundering herd").

## Circuit breaker

Without a breaker, a down server means every sync run spends 3 retries × timeouts per scan, drains the battery and holds the sync loop.
With it, after the failure threshold the circuit **opens**: further calls fail immediately (no network used). After 30 s it goes
**half-open**: one trial request; success closes it, failure re-opens it. The state is exposed through `ICircuitStateMonitor`;
the app shows a red *"Server unavailable: will retry shortly"* banner and the Sync page shows the state.

Because the breaker is *inside* the retry, each failed attempt counts toward opening it, and once it is open a retry gets a
`BrokenCircuitException` immediately, which the uploader turns into `UploaderUnavailableException`; the outbox then stops and leaves the scans Pending.

## Offline and sync flow

1. **Save**: `NewScanPage` writes the scan to Couchbase Lite (status *Pending*) and shows the result immediately, online or not.
2. **Detect**: `MauiConnectivityService` wraps MAUI `Connectivity`. Offline shows an orange banner on every page and the sync loop idles.
3. **Sync** (`SyncCoordinator`): runs every 30 s, and immediately when a scan is saved or the network returns.
   One sync at a time (a semaphore). It calls the Part A `OutboxService`, which hands batches to `ApiShipmentUploader`.
4. **Upload** groups scans by tracking number (the API route is per shipment) and maps outcomes:
   accepted or duplicate → *Synced*; rejected / 400 / 404 / 409 / 403 → *Failed* with the reason; unreachable / 401 / open circuit → stays *Pending*.
5. **Recover**: when the server is back, the next run (or the half-open trial) uploads everything in order. Nothing was lost,
   nothing is duplicated (tested: `Failure_then_recovery_leaves_nothing_lost`, `Duplicate_is_a_success_so_resending_is_safe`).

## Storage and secrets

* Sign-in: the courier enters a **Courier ID** (default `C-1001`) and the app calls `POST dev/token` with `{ "courierId", "role": "Courier" }`,
  which only exists while the API runs in Development and returns `{ accessToken, expiresAtUtc }`. A JWT can also be pasted.
  The token's `courierId` claim is the courier id stored on every scan.
* The JWT is kept in `SecureStorage` (Android Keystore-backed, Windows data protection), never in Preferences, files or the scan database.
  If the secure store is unavailable (some unpackaged Windows setups) it is held in memory only: the user signs in again next launch.
* Transport is HTTPS only. Debug builds accept the ASP.NET Core development certificate for `localhost` / `10.0.2.2` only;
  release builds validate certificates normally.
* The base URL is chosen per platform: Windows `https://localhost:7153/`, Android emulator `https://10.0.2.2:7153/`
  (the emulator's alias for the PC's localhost).

## JSON source generation

`ApexJsonContext` (`System.Text.Json` source generator) generates the serializers for every request and response at build time.
No reflection at run time: faster start-up on a phone and safe with trimming / AOT (MAUI release builds trim). The generic
`JsonStringEnumConverter<ApiScanType>` keeps enums as strings without reflection.

## Tests

`ApexPost.Courier.Core.Tests` runs the **real** pipeline (client, auth handler, retry, circuit breaker) on a fake network:

```powershell
dotnet test .\ApexPost.Courier.Core.Tests
```

It proves: request shape and bearer header; 5xx and network errors are retried (3 calls); 400/404 are not (1 call);
transient-then-success ends in success; the circuit opens and stops calling the server; 401 clears the token;
offline sync does nothing and scans stay Pending; failure then recovery loses and duplicates nothing.
