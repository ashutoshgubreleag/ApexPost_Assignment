# Copilot Log: AI-assisted test drafting with human review (Week 5, Task 6)

**Tool:** GitHub Copilot Chat (agent mode) in Visual Studio 2026, run by the author.
**Rule we followed:** Copilot drafts, a human decides. Nothing Copilot wrote was accepted because it passed. Every
test was read, run, judged (kept / changed / rejected, with a reason), and at least two accepted tests were
**proven able to fail** by breaking the production code on purpose.

**Evidence kept in the repository:**

| What | Where |
|---|---|
| Copilot's raw output, exactly as written | `docs/copilot/raw/` (stored as `.txt` so it does not compile) |
| The reviewed versions | `ApexPost.UnitTests/Sync/SyncCoordinatorTests.cs`, `ApexPost.Specs/Features/ShipmentStatus.feature`, `ApexPost.Specs/StepDefinitions/ShipmentStatusSteps.cs` (each starts with a `Copilot-assisted` comment) |
| Break-the-code proofs (mutation diff, red output, green output) | `docs/copilot/proofs/` |

---

## Prompt 1: unit tests for a service (`SyncCoordinator`)

**Why this target:** `SyncCoordinator` (Week 4 Part D) had no isolated unit tests. It mixes interface
dependencies (`IScanRepository`, `INetworkStatus`, `ICircuitStateMonitor`), a `TimeProvider`, and a **sealed**
`OutboxService`, a classic trap for generated mocks.

**Context files opened in the editor:** `SyncCoordinator.cs`, `OutboxService.cs`, `IScanRepository.cs`,
`INetworkStatus.cs`, `CircuitStateMonitor.cs`.

**Prompt (exact):**

```text
Write xUnit unit tests for the SyncCoordinator class using Moq.
Cover: syncing while offline, a successful sync that uploads pending scans,
a sync interrupted because the server is unavailable, two syncs started at the
same time (only one should run), and the Changed event / snapshot values
(PendingCount, LastAttemptUtc, LastSuccessUtc, LastMessage).
Use the Arrange-Act-Assert pattern and name tests Method_Scenario_ExpectedResult.
Do not use DateTime.Now; use a fake TimeProvider.
```

**What Copilot produced:** 5 tests in a new `ApexPost.Courier.Core.Tests/SyncCoordinatorTests.cs`, plus a Moq
package reference added to `ApexPost.Courier.Core.Tests.csproj`. Copilot reported "5 passed, 0 failed", and we
confirmed it. Raw: `docs/copilot/raw/prompt1-SyncCoordinatorTests.cs.txt`, `prompt1-csproj.diff.txt`.

**What Copilot did well:** it did **not** try to mock the sealed `OutboxService` (it built the real one over mocked
ports), it used AAA sections, avoided `DateTime.Now`, and its concurrency test was deterministic
(`TaskCompletionSource` gates instead of `Task.Delay`).

### Review decisions

| # | Copilot test | Decision | Reason |
|---|---|---|---|
| 1 | `SyncNowAsync_OfflineScenario_ReturnsNull` | **Changed** | `CountPending` was answered from `Queue([2, 2])`, which encodes *how many times* the code calls `CountPending`. That is a **test that copies the implementation**, and it breaks on a harmless refactor. Replaced with a state-driven fake (pending = scans not yet Synced). Added the missing check that the local store is never read while offline (`GetPending` `Times.Never`). |
| 2 | `SyncNowAsync_SuccessfulSync_UploadsPendingScans` | **Changed** | Same call-count `Queue([1, 0])` coupling, replaced by state. The tracking number `APX-2001` does not match the real `APX-########` format; replaced with fictional but realistic data. |
| 3 | `SyncNowAsync_ServerUnavailableScenario_InterruptsAndKeepsPending` | **Kept** (renamed to `…_ServerUnavailable_InterruptsAndKeepsScansPending`, uses the shared fixture) | Correct and meaningful: proven able to fail (proof 1 below). |
| 4 | `SyncNowAsync_ConcurrentSyncs_OnlyOneRuns` | **Changed** | `Assert.Equal(2, results.Length)` is a **tautology**: `Task.WhenAll` over two tasks always returns two results, so it can never fail. Removed. The second sync is now awaited *while the first is still uploading*, and asserted `null` directly. |
| 5 | `SyncCoordinator_ChangedEvent_UpdatesSnapshotValues` | **Rejected** (replaced) | (a) The name does not follow `Method_Scenario_ExpectedResult`. (b) `Assert.NotEmpty(snapshots)` is a **weak assertion**. (c) It only checked the *last* snapshot, which repeats test 2's assertions, so it never tested the **event sequence** its name promises. Replaced by a human-written `SyncNowAsync_WhileRunning_PublishesSyncingThenIdleSnapshots`, which asserts exactly two events: first `IsSyncing = true` with the scan still pending, then `IsSyncing = false` with 0 pending. That is what the Sync Status page actually depends on. |
| – | *Location* | **Changed** | Copilot put the tests in `ApexPost.Courier.Core.Tests`, which our strategy reserves for tests over the **real Polly pipeline**, and added Moq there. Moved to `ApexPost.UnitTests/Sync/` (Moq and `FakeTimeProvider` already referenced) and reverted the `.csproj` change, so the test pyramid layout stays honest. |
| – | *Fake clock* | **Changed** | Copilot hand-wrote a `FixedTimeProvider` that can never move. Replaced by `FakeTimeProvider`, which can `Advance()`. That is needed for the missing edge case below. |

### Missing edge cases added in review (human-written, marked in the file)

* `SyncNowAsync_ServerUnavailableAfterEarlierSuccess_KeepsLastSuccessAndMovesLastAttempt`: after a success at
  09:15, a failed attempt at 09:25 must show *last attempt 09:25, last success 09:15*. Copilot's frozen clock made
  this impossible to express.
* `SyncNowAsync_CallerCancelsDuringUpload_RethrowsAndStopsShowingSyncing`: cancelling must not leave the spinner
  on.
* `CircuitStateChanged_Opened_IsPublishedInTheSnapshot`: the circuit state shown on the page follows the monitor
  (Moq `Raise`).

Result: **8 tests** (3 changed, 1 kept, 1 rejected and replaced, 3 added), all passing.

---

## Prompt 2: a Gherkin feature with step definitions (`ShipmentStatus.feature`)

**Context files opened:** `ShipmentCreation.feature`, `ShipmentCreationSteps.cs`, `ShipmentWorld.cs`, `Hooks.cs`.

**Prompt (exact):**

```text
Write a Reqnroll Gherkin feature file ShipmentStatus.feature in business language
about the shipment status lifecycle (Created, PickedUp, InTransit, OutForDelivery,
Delivered, Failed). Include a Background and at least one Scenario Outline with
Examples. Cover: the normal forward path, a delivered shipment cannot go back to
in transit, and skipping a step is refused. Then write the C# step definitions
class for it, reusing the existing ShipmentWorld context via constructor injection.
```

**What Copilot produced:** `ShipmentStatus.feature` (Background, one Scenario Outline with 5 examples, 2 scenarios,
7 test cases in total) and `ShipmentStatusSteps.cs`. Copilot reported "7 passed, 0 failed", and we confirmed it.
Raw: `docs/copilot/raw/prompt2-ShipmentStatus.feature.txt`, `prompt2-ShipmentStatusSteps.cs.txt`.

**What Copilot did well:** it reused `ShipmentWorld` through **constructor injection** (no static state), and it
used `world.TryAsync` so a refused change is recorded for the Then step instead of crashing the When step.

### Review decisions

| # | Copilot item | Decision | Reason |
|---|---|---|---|
| 1 | `AdvanceToStatusAsync` helper (used by "has already been moved to …") | **Rejected** (replaced) | **A latent bug, proven.** It walked a single list `[Created, PickedUp, InTransit, OutForDelivery, Delivered, Failed]`, so reaching *Failed* went **through Delivered**, and Delivered → Failed is illegal. It only passed because no example started from Failed. Probe run with Copilot's raw steps: `ConflictException : Cannot move shipment APX-10000001 from Delivered to Failed.` (`docs/copilot/proofs/rejected-helper-probe.txt`). It also **copied implementation logic** (it re-derived the status order). Replaced by an explicit table of business journeys (`PathFromCreated`), one readable path per target status. |
| 2 | Two Then steps: "…cannot move backwards" and "…must progress one step at a time" | **Changed** (merged) | Both made **identical assertions** (`ConflictException` with `INVALID_STATUS_TRANSITION`). The wording promised a distinction nothing checked, and production does not make that distinction either. Merged into one honest step, "the status change is refused", always followed by "the status is still X". |
| 3 | Binding `the shipment is currently "…"` | **Rejected** | Dead code: no scenario used it. |
| 4 | Feature wording | **Changed** | Copilot used enum identifiers (`OutForDelivery`, `InTransit`), which is not business language. Changed to "out for delivery", "in transit", and so on, with one `Parse` that maps words to the enum. Tabs normalised to spaces. |
| 5 | Background, forward-path Scenario Outline, the Delivered → InTransit scenario, the skipping scenario | **Kept** (wording updated) | Correct and meaningful: the Delivered → InTransit scenario is proven able to fail (proof 2 below). |
| – | *Missing edge cases* | **Added** | A second Scenario Outline, "Moves that break the lifecycle are refused" (delivered → in transit, failed → delivered, created → delivered, picked up → created), each with a business reason column. Also "A late depot scan does not reopen a delivered shipment", which goes through the scan upload path, not just `ChangeStatus`. |

Result: **12 scenarios** (5 + 1 + 1 from Copilot, reviewed; 4 + 1 added), all passing.

---

## Proof that generated tests are meaningful (break → red → restore → green)

Each mutation was applied to production code in the working tree, the test run, then the file restored with
`git checkout` (`git diff --quiet` confirmed no leftover change). No broken code was ever committed.

### Proof 1: Copilot's kept unit test catches a wrong "last success" time

Mutation (`docs/copilot/proofs/proof1-mutation.diff`), in `ApexPost.Courier.Core/Sync/SyncCoordinator.cs`:

```diff
-                LastSuccessUtc = result.Interrupted ? s.LastSuccessUtc : now,
+                LastSuccessUtc = now, // MUTATION (proof 1): success recorded even when interrupted
```

Red (`proof1-red.txt`):

```
SyncCoordinatorTests.SyncNowAsync_ServerUnavailable_InterruptsAndKeepsScansPending [FAIL]
   Assert.Null() Failure: Value of type 'Nullable<DateTimeOffset>' has a value
Expected: null
Actual:   2026-03-02T09:15:00.0000000+00:00
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

Restored, green (`proof1-green.txt`): `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`

**What it proves:** the test checks the *meaning* of the snapshot (an interrupted sync is not a success), not just
that a method ran. In production this bug would tell a courier "last synced just now" while every scan is still
on the phone.

### Proof 2: Copilot's scenario catches a delivered parcel being reopened

Mutation (`docs/copilot/proofs/proof2-mutation.diff`), in `ApexPost.Models/Domain/Tracking.cs`:

```diff
-        [TrackingStatus.Delivered] = [],
+        [TrackingStatus.Delivered] = [TrackingStatus.InTransit], // MUTATION (proof 2): delivered parcels may reopen
```

Red (`proof2-red.txt`):

```
A delivered shipment cannot be moved back to in transit [FAIL]
   Assert.IsType() Failure: Value is null
Expected: typeof(ApexPost.Models.Exceptions.ConflictException)
Actual:   null
 -> done: ShipmentStatusSteps.GivenTheShipmentHasAlreadyReached("delivered")
 -> done: ShipmentStatusSteps.WhenTheShipmentIsMovedTo("in transit")
 -> error: Assert.IsType() Failure: Value is null
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

Restored, green (`proof2-green.txt`): `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`

**What it proves:** the business scenario protects risk R1 (status transitions) end to end through the service,
in words the dispatch team can read.

---

## Summary and lessons

| | Prompt 1 (unit) | Prompt 2 (BDD) |
|---|---|---|
| Generated | 5 tests | 7 scenarios + 4 bindings |
| Kept | 1 | 4 scenarios / 3 bindings (reworded) |
| Changed | 3 + location + fake clock | 2 (merged Then steps, business wording) |
| Rejected | 1 (event test) | 2 (buggy helper, dead binding) |
| Added by human | 3 | 5 scenarios |
| Proven able to fail | 1 (proof 1) | 1 (proof 2) |

* **"All green" from Copilot meant very little.** Both outputs passed on the first run, yet contained a
  tautological assertion, a weak `NotEmpty`, a test that re-checked another test's state, a call-count-coupled
  fake, two assertions disguised as different rules, dead code, and a latent bug that only appeared on an
  untested path.
* **Copilot is good at structure** (AAA, mocks over ports, async gates, constructor injection) and weak at
  **intent**: what exactly should fail if the rule breaks. That part stayed human work.
* **The most valuable review question** was "what production bug would make this test fail?" For the rejected
  event test the honest answer was "none that test 2 doesn't already catch".

**Viva example (a rejected Copilot test):** `SyncCoordinator_ChangedEvent_UpdatesSnapshotValues`. Its name
promised to test the `Changed` events, but it only asserted `NotEmpty` and then re-checked the final state, so a
coordinator that never published "syncing…" would still pass. It was replaced with a test that asserts the exact
two-event sequence.
