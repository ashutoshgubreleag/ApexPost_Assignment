# Offline design – ApexPost Courier (Part A)

## Why local first
A courier scans parcels in vans, lifts and rural dead zones. If saving a scan needs the network, the
scan is lost or the courier waits. So every scan is **written to the local Couchbase Lite database
first** (`syncStatus = Pending`) and the UI returns immediately. The outbox (`OutboxService`) uploads
Pending scans later, in batches of 20, through `IShipmentUploader`. Only after the server accepts a
scan is it marked `Synced`; a server rejection marks it `Failed`; an unreachable server changes
nothing (scans stay `Pending`, retried next time). A scan therefore exists in exactly one durable
place at every moment – nothing is "in memory only". The upload is safe to repeat because scan ids
(`scan::{Guid}`) are generated on the device and the server de-duplicates by id (Part B).

## Layout
* `Domain` – `ShipmentScan`, `IScanRepository`, `IShipmentUploader` (no Couchbase types; checked by a test).
* `Application` – `OutboxService`, `FakeShipmentUploader` (replaced by the real API client in Part D).
* `Infrastructure/Couchbase` – `CourierDatabase`, `CouchbaseScanRepository`, `PendingScanMonitor`.
* Reads by id use key-value access; everything else uses three parameterized SQL++ queries backed by
  value indexes on `trackingNumber` and `syncStatus`.

## Conflicts: last-write-wins vs the server decides
Here a scan is an **immutable fact** ("parcel X was scanned at T by courier C"); couriers add scans,
they rarely edit them. Two devices cannot meaningfully conflict on the same document id, because each
scan has its own Guid. The only real conflict is on shipment *state* (two couriers report different
outcomes for the same parcel).
* **Last-write-wins** (by `scannedAtUtc`) is simple but trusts device clocks, which drift, and silently
  discards a scan that may be the true one.
* **Server decides** keeps every scan as history and lets the server apply business rules (e.g. a
  Delivered scan beats a later InTransit scan; Failed needs a reason). The device only reports.

We choose **the server decides**: devices upload facts, the server derives the shipment's current
status, and a rejected scan comes back as `Failed` for a human to review. LWW is acceptable only for
free-text fields such as `notes` on an unsynced scan.

## Why an embedded database is not shared between processes
Couchbase Lite is a library that runs *inside* one app and locks its database directory; it is not a
server. Two processes opening the same files risk corruption or lock errors. Each app (one courier
device) owns its database exclusively, and sharing happens only through **sync with the server** (REST
in this assignment). That is also why the change listener and the harness live in the same process as
the database.
