# Part D demo video script (about 3 minutes)

Record the screen with Xbox Game Bar (`Win+G`) or OBS. Prepare first: API running (`dotnet run --project .\ApexPost.Api --launch-profile https`),
a shipment `APX-10000001` created (run the first requests of `ApexPost.Api.http`), and the courier app built (`dotnet build .\ApexPost.Courier -f net10.0-windows10.0.19041.0`).

| Time | Show | Say |
| --- | --- | --- |
| 0:00 | Swagger UI / `.http` with the API running over HTTPS | "ApexPost API, JWT-secured. The courier app is .NET MAUI with an offline Couchbase Lite store." |
| 0:20 | App → Courier ID `C-1001` → "Get development token" | "The API issues a development token for the courier ID (Development only). The JWT is kept in SecureStorage, not in a file or the scan database." |
| 0:35 | New scan: APX-10000001, Pickup, "Melbourne Depot", Save | "Saved locally first; the upload happens in the background." |
| 0:50 | Pending tab (empty a moment later) and Sync tab (last success time) | "It synced: pending is 0." |
| 1:05 | Turn the network off (Wi-Fi off, or the Android emulator's airplane mode) | "Now offline." Show the orange banner. |
| 1:15 | Save two scans (InTransit, then Delivered with signature name in Notes) | "Saving still works instantly: written to the local database." |
| 1:40 | Pending tab shows 2 scans; Sync tab says offline | "They wait in the outbox, nothing is lost." |
| 2:00 | Network on | "Back online." Banner disappears. |
| 2:10 | Pending tab goes to 0; in Swagger `GET /api/v2/shipments/APX-10000001/scans` lists the 3 scans | "Synced automatically, once each." |
| 2:30 | Stop the API; save a scan; open the Sync tab | "Server down: the retries and circuit breaker; the red banner. Scan stays pending." |
| 2:50 | Start the API again, press Sync now | "Circuit closes, the scan uploads. Nothing duplicated: the API de-duplicates on scanId." |
| 3:00 | End | "Retry with backoff and jitter, circuit breaker, source-generated JSON, SecureStorage. Details in RESILIENCE.md." |

Tips: keep the Sync tab visible in step 2:30 so the *Circuit: Open* line shows. With default settings the circuit needs 4 failed calls to open;
press "Sync now" a few times while the API is stopped.
