# ApexPost Testing Strategy (Week 5)

> **Goal.** Give the engineering lead evidence that ApexPost is reliable and safe to change before the next release.
> The goal is *not* the largest number of tests. It is a deliberate strategy where every test exists because it
> defends against a named risk, at the cheapest level that can actually catch that risk.
>
> Every section below states **what** we decided and **why** (the justification). Where we chose *not* to do
> something, the reason is written down too.

Status: approved strategy, written before any new Week 5 test code (Task 1).
Branch: `week5-testing`.

---

## 0. Starting point (baseline)

Before deciding anything we measured what already exists:

| Test project | Tests (cases) | Level today | Result |
|---|---:|---|---|
| `ApexPost.Tests` | 143 | Unit (hand fakes) + MVC `WebApplicationFactory` | 143 passed |
| `ApexPost.Api.Tests` | 157 | Service tests (stateful fake) + API pipeline (`WebApplicationFactory`, in-memory store) | 157 passed |
| `ApexPost.CounterTerminal.Tests` | 31 | Integration on a real Couchbase Lite database | 31 passed |
| `ApexPost.Courier.Offline.Tests` | 31 | Integration on a real Couchbase Lite database | 31 passed |
| `ApexPost.Courier.Core.Tests` | 24 | Client + Polly pipeline over a stub `HttpMessageHandler` | 24 passed |
| `ApexPost.IntegrationTests` | 46 | SQL Server (LocalDB), disposable migrated databases | 46 passed |
| **Total** | **432** | | **432 passed, 0 failed, 0 skipped** |

What the baseline review found:

* **No mocking library, no Reqnroll/SpecFlow, no SQLite.** All isolation was done with hand-written fakes.
* **About 20 test call sites use the real clock** (`DateTime.UtcNow` / `DateTimeOffset.UtcNow`). That breaks the
  determinism constraint: a test that builds "a scan one hour ago" or "an expired token" from the wall clock can
  behave differently depending on when and where it runs.
* **One timing assertion** (`cacheMs < repositoryMs`, measured with `Stopwatch`) can fail on a busy CI agent even
  when the code is correct.
* **Fixed sleeps** (`Task.Delay(300)` / `Task.Delay(500)`) in two change-listener tests.
* **Mixed naming styles** (`Method_Scenario_Result` and snake_case sentences).

*Why measure first:* the assignment says to review existing tests before adding new ones. A green baseline also
means any later failure was caused by Week 5 changes, not by something already broken.

---

## 1. Risk map

We ranked components by **likelihood of breaking** (how often the code changes, how complex it is, how many
external factors affect it) and **cost if it breaks** (customer impact, data loss, security).

| # | Risk | Where it lives | Likelihood | Cost | Why it is on the map |
|---|---|---|---|---|---|
| **R1** | **Illegal shipment status transitions** (backwards, skipping a step, leaving a final state) | `TrackedShipment.CanMoveTo/ChangeStatus/AdvanceFor` | Medium | **High** | Customers and partners see the status. A shipment shown as "Delivered" and then "InTransit" causes support calls and possible payout disputes. The rule is a table that is easy to "fix" wrongly. |
| **R2** | **Duplicate tracking numbers**, including two creates that race | `ShipmentTrackingService.CreateAsync`, unique index `UQ_TrackedShipments_TrackingNumber`, `UnitOfWork` SqlException → `DuplicateKeyException` mapping | Medium | **High** | A duplicate number sends scans to the wrong parcel. The race path (check passes, insert fails) only runs under concurrency, so it is rarely exercised by hand. |
| **R3** | **Scan de-duplication / idempotent re-upload** | `UploadScansAsync` (in-batch duplicates, already stored, owned by another shipment, race on `UQ_ShipmentScans_ScanId`) | **High** | **High** | Couriers are offline and their apps retry, so the same batch *will* be sent more than once. A missed duplicate creates a second "Delivered" event, and a wrong rejection loses a real scan. |
| **R4** | **JWT role and ownership rules** | `ApexPost.Api/Security/*` (Courier vs Admin, `ScanOwnershipFilter`, partner key) | Medium | **Critical** | A security regression (a courier uploading for another courier, an admin changing statuses, a partner key opening JWT routes) is the most expensive kind of bug, and attribute and filter wiring is easy to break in a refactor. |
| **R5** | **Client retry and error mapping** | `ApexApiClient`, `ApiShipmentUploader`, `ResilienceExtensions` | Medium | **High** | If 4xx is retried, the server gets hammered. If 5xx or a timeout is treated as a rejection, a scan is wrongly marked Failed and **lost**. If ProblemDetails text is lost, the courier gets an unhelpful message. |
| **R6** | **Outbox losing scans** on network failure or cancellation | `OutboxService.SyncPendingAsync` | Medium | **Critical** | The outbox is the only path from phone to server. If a scan is wrongly marked Synced, it is gone for good. |
| **R7** | **Validation boundaries** (weight 0.1–70 kg, `APX-########`, phone format, 1–100 scans, signature on Delivered) | `ShipmentTrackingService.ValidateDetails`, request DTO attributes | **High** | Medium | Off-by-one errors at boundaries are the most common defect class, and these limits are also enforced by database check constraints, so a mismatch shows up as a 500 instead of a 400. |
| **R8** | **Repository query correctness** (filters, paging order, latest scan projection) | `TrackingQueries`, `TrackedShipmentRepository` | Low–Medium | Medium | LINQ that works in memory can fail to translate, or translate differently, on SQL Server. Only a real database shows this. |

*Why these and not others:* the counter-sale, money-order and reporting features from Weeks 2–3 already have
dedicated test suites and are not part of the Week 4/5 courier release. The courier tracking flow (create →
scan offline → sync → status) is new, crosses three processes (phone, API, database), and is where the next
release changes code. Testing effort goes where the change and the risk are.

---

## 2. Test pyramid for ApexPost

| Level | Project | What it covers (risk) | Tools | Target share of the final suite | Why this level |
|---|---|---|---|---|---|
| **Unit** | `ApexPost.UnitTests` (new) + kept unit classes | Status matrix (R1), validation boundaries (R7), `ShipmentTrackingService` with mocked ports (R2, R3), `OutboxService` with mocked repository and uploader (R6), `ApexApiClient` over a fake `HttpMessageHandler` (R5), the TDD rule | xUnit, Moq | **≈ 65–70 %** | Runs in milliseconds, pinpoints the failing rule, and can force paths that are hard to reach for real (a race on a unique index, a timeout, a cancelled token). Most of the business logic is pure or sits behind interfaces, so it is cheap to test here. |
| **Integration** | `ApexPost.IntegrationTests` (extended) + kept Couchbase / API pipeline projects | Repositories and queries on **real SQL Server** (R2, R3, R8), **one end-to-end API test** with `WebApplicationFactory` + SQL Server test database + test authentication (R2, R3, R4), Couchbase Lite repository (R6), existing JWT security tests (R4) | xUnit, `Microsoft.AspNetCore.Mvc.Testing`, LocalDB, Couchbase Lite | **≈ 25 %** | Unit tests with mocks prove our logic *given* the dependencies behave as we assume. Integration tests prove those assumptions hold: SQL translation, unique indexes, cascades, routing, model binding, auth middleware. They are slower, so we keep them to the seams that mocks cannot prove. |
| **BDD / acceptance** | `ApexPost.Specs` (new) | Business scenarios: shipment creation, offline scan sync (R1, R2, R3, R7) | Reqnroll + xUnit | **≈ 5–8 %** | Documents the agreed behaviour in language the operations team can read and sign off. Few scenarios on purpose: they repeat behaviour already covered lower down, so their value is communication and acceptance, not extra coverage. |

*Why a pyramid and not a "trophy" or "ice-cream cone":* ApexPost's core risk is in **rules** (status machine,
de-duplication, error mapping), which unit tests check fastest and most precisely. The integration layer is
deliberately thin but **real** (real SQL Server, real HTTP pipeline), so it catches what mocks cannot. We have
no UI tests (out of scope), so there is no ice-cream-cone top.

---

## 3. Framework choice: **xUnit** (one framework for the whole solution)

| Criterion | xUnit | NUnit | Why it matters here |
|---|---|---|---|
| **Consistency with existing tests** | All 432 existing tests are xUnit 2.9.3 | Would need rewriting or a second framework | One framework means one set of attributes, one runner configuration and no context switching. Rewriting 432 green tests would add risk and prove nothing new. |
| **Test isolation** | New class instance **per test**, no shared state by default | One instance per fixture by default; state leaks unless `[SetUp]` resets it | The constraint "no shared mutable state, no reliance on test order" is enforced by xUnit's design instead of by discipline. |
| **Setup / teardown** | Constructor + `IDisposable` / `IAsyncLifetime`; `IClassFixture` / `ICollectionFixture` for expensive things | `[SetUp]`, `[TearDown]`, `[OneTimeSetUp]` | Plain C# constructors make dependencies explicit, and async lifetime suits our async database setup. Fixtures let us share one `WebApplicationFactory` or SQL database per class, where creation is expensive and the state is controlled. |
| **Parameterized tests** | `[Theory]` + `[InlineData]`, `[MemberData]`, `TheoryData<>` | `[TestCase]`, `[TestCaseSource]` | Both are good. `TheoryData<TrackingStatus, TrackingStatus>` gives a strongly typed 36-pair status matrix. |
| **Reqnroll support** | `Reqnroll.xUnit` (first-class) | `Reqnroll.NUnit` (first-class) | No difference, so it does not affect the choice. |
| **Runner integration** | Visual Studio Test Explorer + `dotnet test` via `xunit.runner.visualstudio` (already configured) | Same | No difference. |
| **Team familiarity** | Used in Weeks 2–4 | Not used yet | Lower learning cost and faster reviews. |

**Decision: xUnit 2.9.3 everywhere** (`ApexPost.Specs` uses `Reqnroll.xUnit`).

*Why not move to xUnit v3 now:* v3 changes packages, the runner model and some APIs. That is a migration with
risk and no benefit for the risks in §1. It can be a separate, later change.

*Why Moq (and not NSubstitute or more hand fakes):* the assignment requires Moq. Beyond that, Moq's
`Verify(..., Times.Never)` states **interaction** rules directly ("an invalid command never reaches
`SaveChangesAsync`"), which a hand fake can only show indirectly. We **keep** the existing stateful fakes where
behaviour depends on *state* (see §4). The two tools answer different questions.

---

## 4. Existing tests review

Legend: **Kept** = unchanged · **Modified** = changed in place (reason given) · **Removed** = deleted ·
**Replaced** = superseded by a new test elsewhere.

**No test is removed.** *Why:* every existing class still guards a real behaviour, the full suite runs in about
a minute, and deleting a passing regression test just to show a "removed" row would reduce safety. Where a test
had a defect (real clock, timing, sleep) we **fixed** it rather than deleted it.

### 4.1 `ApexPost.Api.Tests` (courier tracking API, in scope)

| Class | Decision | Reason |
|---|---|---|
| `ApiEndpointTests` | **Modified** | Built scan times with `DateTimeOffset.UtcNow`. Changed to fixed instants from `Support/TestTimes`: a fixed past reference for "already scanned", and the year 2999 for "a scan in the future", so the outcome is the same on every run. *Why not register a fixed `TimeProvider` in `ApiFactory`:* the JWT bearer handler validates token lifetimes against the real clock, so tokens issued by a frozen clock would be rejected as expired. Fixed instants that are unambiguous against any real clock remove the wall-clock dependency without fighting the framework. The HTTP-level assertions (201/400/404/409/415, ProblemDetails, idempotent re-upload) stay as they are. |
| `ApiVersioningTests` | **Modified** | Same real-clock issue in scan payloads, same fix (fixed instants that keep the original ordering: one hour and two hours before the reference). The contract assertions (v1 flat vs v2 nested, deprecation headers, OpenAPI per version) are kept. |
| `RequestValidationTests` | **Modified** | The future-scan and clock-drift checks used the wall clock. The production validator already reads `TimeProvider` from the `ValidationContext`, so the test now supplies a fixed clock. Its parameterized boundaries (weight, phone, tracking number, 1–100 scans) already match our strategy. |
| `SecurityTests` | **Modified** | Expired, forged-key and wrong-audience tokens were built from `DateTime.UtcNow`. The expired token now lives entirely in the year 2000. The forged and wrong-audience tokens are valid from 2000 to 2999, so the **only** possible reason for their 401 is the key or the audience. The dev-token test compared `expiresAtUtc` with the wall clock; it now compares it with the token's own `exp` claim and checks `exp > iat`, which is deterministic and a stronger check. The role and ownership tests (R4) stay as they are. |
| `ShipmentTrackingServiceTests` | **Kept** (support fixture modified) | A sociable service test over `InMemoryTrackingStore`, a *stateful* fake that copies EF Core's snapshot, unique-index and discard behaviour. That is the right tool for **behaviour over state** (status walks, idempotent re-upload). Moq could not express it without re-implementing the store. Only `InMemoryTrackingStore.InjectScan` changes, from `UtcNow` to a fixed time. New Moq tests in `ApexPost.UnitTests` complement it with **interaction** checks; they do not duplicate it. |
| `ApiExceptionHandlerTests` | **Kept** | Pure and deterministic. It proves the exception → status → ProblemDetails mapping and that 500 responses never leak internals (R4/R5). |

### 4.2 `ApexPost.Courier.Core.Tests` (Week 4 Part D client, in scope)

| Class | Decision | Reason |
|---|---|---|
| `UploaderTests` | **Modified** (fixture) | The shared `Host.AddScan` helper uses `DateTimeOffset.UtcNow`, which is changed to a fixed time. Otherwise kept: these tests run the **real Polly pipeline** (retry, circuit breaker, 4xx not retried). The new `ApexApiClient` unit tests do not cover that layer. |
| `SyncAndAuthTests` | **Modified** (fixture) | Same fixture fix. The bounded polling loop (`Task.Delay(20)` inside a 10 s `CancellationTokenSource`) is kept, because it waits *for a condition*, not for a fixed time, so its outcome is deterministic. |

### 4.3 `ApexPost.Courier.Offline.Tests` (Week 4 Part A outbox, in scope)

| Class | Decision | Reason |
|---|---|---|
| `OutboxTests` | **Kept** | Runs the outbox against a **real Couchbase Lite** store, which proves Pending → Synced/Failed is actually persisted. The new Moq tests prove the interactions (which scans are updated, how often the uploader is called), so the two complement each other. |
| `RepositoryTests` | **Kept** | Repository logic against the real embedded database (document id format, always Pending on create, survives restart). Mocking the store would prove nothing about persistence. |
| `QueryTests` | **Kept** | Proves the SQL++ queries, ordering, parameterization and index usage on the real engine. |
| `ChangeListenerTests` | **Modified** | `Task.Delay(500)` "wait and see that no event arrived" is replaced with a **positive control**: a second, live monitor on the same database. The test waits (bounded, on a `TaskCompletionSource`) until the control has received the change, which proves the notification was delivered, and only then asserts that the disposed monitor saw nothing. This waits for a condition that is known to happen instead of sleeping and hoping. |
| `DomainPurityTests` | **Kept** | Architecture guard: domain and application code never reference Couchbase types. Cheap and stable. |

### 4.4 `ApexPost.IntegrationTests` (SQL Server, in scope for repositories)

| Class | Decision | Reason |
|---|---|---|
| `SqlServerPersistenceTests` | **Kept** | Already follows our data-access strategy (§5): real migrations, constraints, rowversion, procedures. |
| `QueryTranslationTests` | **Kept** | Runs every query-service method on SQL Server, which is exactly the "LINQ translates" proof that mocks cannot give. |
| `ReviewHardeningTests` | **Kept** | Proves the SqlException → `DuplicateKeyException` mapping (which R2/R3 rely on) and the database permission hardening. |
| `SqlFolderScriptTests` | **Kept** | Week 3 SQL deliverables still run. Cheap regression check. |
| `ToQueryStringEvidenceTests` | **Kept** | Asserts the generated SQL is parameterized and has the expected shape (a security and performance guard). Needs no database server. |
| `TransactionRuleTests` (linked from `ApexPost.Tests`) | **Kept** | The same source runs against fakes and against SQL Server, which proves the persistence refactor kept the behaviour. |

### 4.5 `ApexPost.Tests` (Weeks 2–3: counter sales, MVC; regression net)

These cover the counter-sale web app, which is **outside the Week 5 courier scope**. They are kept as a cheap
regression net (5 s for 143 cases). Changes are limited to direct real-clock or timing defects inside test bodies.

| Class | Decision | Reason |
|---|---|---|
| `StockRuleTests` | **Modified** | `DateTimeOffset.UtcNow` passed as an irrelevant timestamp → fixed constant (the value never affects the assertion, but the determinism rule applies to every test). |
| `MoneyOrderIdentityRuleTests` | **Kept** | Rule tests, already parameterized on the threshold boundary. |
| `ParcelWeightLimitTests` | **Kept** | Parameterized boundary tests. They match our strategy. |
| `PricingTests` | **Kept** | Pure calculations. |
| `ExceptionMappingTests` | **Kept** | Pure and parameterized. |
| `ValueObjectTests` | **Kept** | Pure value-object rules with exception tests. |
| `TaxRuleTests` | **Kept** | Pure and parameterized. |
| `ReflectionValidatorTests` | **Kept** | Pure. |
| `SpecificationTests` | **Modified** | `UtcNow` timestamps → fixed constant. |
| `AuditRedactorTests` | **Kept** | Pure and parameterized (security relevant). |
| `ConcurrencyAndCachingTests` | **Modified** | `ReferenceDataCache_HitsCompleteSynchronously_AndAreFaster` asserted `cacheMs < repositoryMs` using `Stopwatch`. A performance comparison inside a unit test is **non-deterministic**: GC pauses or a busy CI agent can flip it while the code is correct. We keep the meaningful functional assertion (a cache hit completes synchronously, `IsCompletedSuccessfully`) and drop the timing assertion. Performance evidence belongs in `PERFORMANCE.md`. The audit `UtcNow` timestamp → fixed constant. |
| `OfflineSyncAndUnitOfWorkTests` | **Kept** | Commit-once and idempotency rules through the unit of work. Still valid. |
| `TransactionRuleTests` | **Kept** | Business rules (also run on SQL Server, see 4.4). |
| `ModelSnapshotTests` | **Kept** | Database-free guard that migrations match the model. Catches a forgotten migration in milliseconds. |
| `AuthorizationAndAuditTests`, `LoginRateLimitTests` | **Kept** | MVC security regression (out of Week 5 scope, cheap to keep). |
| `IntegrationEndpointTests`, `IntegrationNotConfiguredTests`, `PartnerRateLimitTests` | **Kept** | Partner endpoint security regression. |
| `OverPostingTests` | **Kept** | Mass-assignment security regression. |
| `PipelineTests`, `MaintenanceModeTests` | **Kept** | Middleware regression. |
| `SyncApiTests`, `TerminalToServerTests` | **Kept** | Counter-terminal sync regression. |

**Accepted residual risk:** the shared Week 2 seed (`Support/TestData.cs`) still creates data "yesterday"
relative to the real clock, because report queries in that legacy harness filter by date relative to the
current business date. Fixing it means threading a `TimeProvider` through the Week 2 harness, a cross-cutting
change outside the courier scope. It is recorded here and in the reflection instead of being hidden. *Why
acceptable:* the offsets are a full day, so the result can only change if a run straddles midnight UTC, and no
assertion depends on the exact timestamp.

### 4.6 `ApexPost.CounterTerminal.Tests` (Week 2–3 terminal; regression net)

| Class | Decision | Reason |
|---|---|---|
| `ChangeMonitorTests` | **Modified** | `Task.Delay(300)` followed by a 200 ms "nothing else arrives" wait. These collapse into one explicit, documented bounded wait (same reasoning as `ChangeListenerTests`). |
| `CouchbaseStoreTests` | **Kept** | Real embedded database, deterministic. |
| `SyncWorkerTests` | **Kept** | Uses its own controllable clock and fakes, so it is already deterministic. |
| `TerminalServiceTests` | **Kept** | Offline sale rules on the real local store. |

### 4.7 Naming convention for existing tests

**Decision:** new and **modified** tests use `Method_Scenario_ExpectedResult`. **Kept** classes keep their
current descriptive names.

*Why:* the existing names already state scenario and outcome clearly (for example
`Upload_SameBatchTwice_CreatesNoDuplicates`, `Rejected_sale_is_not_retried_and_its_units_are_released`).
Renaming about 400 unchanged tests would create a huge diff with zero behaviour change, break links in earlier
docs and screenshots, and make the real Week 5 changes harder to review. The convention is enforced where code
is actually written or touched.

---

## 5. Data access strategy

| Approach | When we use it | What it proves | What it does **not** prove |
|---|---|---|---|
| **Moq of `ITrackedShipmentRepository`, `ITrackingQueries`, `IUnitOfWork`, `IScanRepository`, `IShipmentUploader`** | Unit tests of services and the outbox | Our decision logic and interactions: an invalid input never saves, a duplicate leads to discard + 409, the uploader is called once per batch, failed scans stay Pending | That the repository's LINQ translates to SQL, that unique indexes or check constraints exist, cascades, rowversion concurrency, transaction boundaries |
| **Stateful hand fake `InMemoryTrackingStore`** (existing) | Service behaviour over state; fast API pipeline tests | Multi-step behaviour (status walks, idempotent re-upload) and the HTTP pipeline (routing, binding, auth, ProblemDetails) without a server | Real SQL semantics (collation, translation, real constraint names, real concurrency) |
| **SQL Server test database** (LocalDB by default; `APEXPOST_TEST_SQL` to override). A disposable `ApexPostTest_<id>` database built with the **real migrations** and dropped afterwards | Repository and query tests; the end-to-end `WebApplicationFactory` test | The production schema: unique indexes (R2/R3), check constraints, cascades, rowversion, SqlException → `DuplicateKeyException` mapping, query translation | Production data volume and performance, Azure SQL or edition-specific behaviour, network latency |
| **SQLite in-memory** | **Not used** | n/a | See below |

**Why SQL Server and not SQLite in-memory.** SQLite is often the easy choice, but for ApexPost it would test a
*different database*:

1. **Check constraints use SQL Server `LIKE` character classes.** For example
   `[TrackingNumber] LIKE 'APX-[0-9][0-9]…'` and `[Sku] LIKE '[A-Z][A-Z][A-Z]-[0-9]…'`. SQLite's `LIKE` treats
   `[0-9]` as literal text, so every valid insert would **fail** the constraint. We would have to strip the
   constraints, and then we would no longer test them.
2. **Rowversion concurrency** (`HasRowVersion`) is generated by SQL Server. SQLite does not generate it, so
   concurrency tests would be meaningless.
3. **Duplicate detection (R2/R3)** depends on `UnitOfWork` mapping `SqlException` numbers 2601/2627 to
   `DuplicateKeyException`. SQLite raises `SqliteException`, so the race path we care about most could **never
   run**.
4. **Migrations contain stored procedures and `datetime2(3)` / `SYSUTCDATETIME()` defaults** that SQLite cannot
   apply, so we would need `EnsureCreated` and would never test the real migrations.
5. LocalDB is already installed with Visual Studio 2026, the existing harness creates and drops databases
   automatically, and the 46 existing SQL tests pass. **No manual setup** is needed: the default connection
   points at `(localdb)\MSSQLLocalDB`.

**Why mocking `DbContext` / `DbSet` gives false confidence.** A mocked `DbSet<T>` is backed by
`IQueryable` over a `List<T>`, so queries run as **LINQ-to-Objects**, not SQL:

* It accepts LINQ that EF Core **cannot translate** (custom methods, some `GroupBy` shapes). The test passes and
  production throws `InvalidOperationException` at run time.
* String comparison follows C# (case-sensitive, ordinal), not the database collation (case-insensitive), so
  filters behave differently.
* **No constraints:** unique indexes, foreign keys, check constraints and `NOT NULL` are never enforced, so
  duplicate tracking numbers (R2) "work".
* **No `SaveChanges` semantics:** no change tracking, no generated keys, no rowversion, no
  `DbUpdateConcurrencyException` or `DbUpdateException`, so the error handling cannot be exercised.
* Include, projection and null-propagation behaviour differs between in-memory and SQL evaluation.
* Async query operators need extra mocking plumbing (`IAsyncQueryProvider`), so the test ends up re-implementing
  EF Core instead of testing ApexPost.

So we mock **our own ports** (`ITrackedShipmentRepository`, etc.), where the contract is ours and small, and
test the **implementations** of those ports against a real SQL Server.

---

## 6. Test project structure

| Project | Status | Contents | Why |
|---|---|---|---|
| `ApexPost.UnitTests` | **New** | Moq service tests, `OutboxService` interaction tests, `ApexApiClient` handler tests, boundary theories, the TDD feature, Copilot-assisted tests | One clear home for fast, isolated tests that run in milliseconds with no I/O. Makes the pyramid's base visible. |
| `ApexPost.IntegrationTests` | **Extended** | Plus tracking repository and query tests, plus one end-to-end API test (`WebApplicationFactory` + SQL Server test database + test authentication) | Already holds the SQL Server harness. Reusing it avoids a second database setup. |
| `ApexPost.Specs` | **New** | Reqnroll features and step definitions | BDD has its own tooling (feature files, generated code, hooks), so it gets its own project. |
| `ApexPost.Api.Tests`, `ApexPost.Courier.Core.Tests`, `ApexPost.Courier.Offline.Tests`, `ApexPost.CounterTerminal.Tests`, `ApexPost.Tests` | **Kept** (documented equivalent structure) | Existing tests, by component | **Why not merge everything into three projects:** moving 432 green tests across projects means changing namespaces, fixtures and project references for no behavioural gain, and a large move hides the real Week 5 changes in review. Each existing project is already at one clear level (see §0 and §2), so the mapping is documented here instead of being forced physically. |

**Level map of every project** (for the viva): Unit → `ApexPost.UnitTests`, the unit parts of `ApexPost.Tests`,
`ShipmentTrackingServiceTests`, `RequestValidationTests`, `ApiExceptionHandlerTests`.
Integration → `ApexPost.IntegrationTests`, the `WebApplicationFactory` classes in `ApexPost.Api.Tests` and
`ApexPost.Tests`, both Couchbase projects, `ApexPost.Courier.Core.Tests` (real Polly pipeline).
BDD → `ApexPost.Specs`.

---

## 7. Conventions every new or modified test follows

| Rule | How | Why |
|---|---|---|
| **AAA** | `// Arrange`, `// Act`, `// Assert` sections; one *act* per test | Readers see what is set up, what is exercised and what is checked. A test with two acts is two tests. |
| **Naming** | `Method_Scenario_ExpectedResult`, for example `CreateAsync_DuplicateTrackingNumber_ThrowsConflict` | The Test Explorer list reads as a specification, and a failure explains itself without opening the code. |
| **Parameterized boundaries** | `[Theory]` with values **just inside, on, and just outside** each limit (0.099 / 0.1 / 70 / 70.001 kg; 0 / 1 / 100 / 101 scans) | Off-by-one errors live at boundaries. One theory covers them all without copy-paste. |
| **Exception tests** | `Assert.Throws[Async]<T>` plus an assertion on the error code or field | Asserting only the type would pass for the wrong reason (for example any `ConflictException`). |
| **Determinism** | No `DateTime.Now/UtcNow`: a fixed `TimeProvider` or fixed instants. No reliance on test order. No static mutable state. No fixed sleeps (bounded waits on conditions only). Seeded or fixed Guids where values are asserted | The constraint requires it. A flaky test is worse than no test, because people learn to ignore red builds. |
| **No real network** | `HttpMessageHandler` fakes in unit tests. `WebApplicationFactory` uses an in-process `TestServer` (no sockets) | The constraint requires it, and it keeps tests fast and repeatable. |
| **Fictional data, no secrets** | Names like "Test Recipient" and `APX-1000000x`. Signing keys are clearly marked test-only constants in test projects. Real keys only in User Secrets | Privacy, plus the constraint. |
| **Copilot marking** | `// Copilot-assisted: drafted by GitHub Copilot, reviewed and edited by the author (see COPILOT-LOG.md)` on each such class | Traceability of AI-generated code, as the task requires. |

---

## 8. Mocking and isolation plan (Task 3)

| Subject under test | Mocked with | Moq features shown | Risk |
|---|---|---|---|
| `ShipmentTrackingService` | `Mock<ITrackedShipmentRepository>`, `Mock<ITrackingQueries>`, `Mock<IUnitOfWork>`, fixed `TimeProvider` | `Setup(...).ReturnsAsync`, `ThrowsAsync(new DuplicateKeyException…)`, `It.Is<TrackedShipment>(s => s.TrackingNumber == …)`, `It.IsAny<CancellationToken>()`, `Verify(…, Times.Once/Never)`, `MockBehavior.Strict` where "no other calls" matters | R2, R3, R7 |
| `OutboxService` | `Mock<IScanRepository>`, `Mock<IShipmentUploader>` | `SetupSequence` for batches, `It.Is<ShipmentScan>(s => s.SyncStatus == Synced)`, `Verify(Update, Times.Never)` when unreachable, `Callback` | R6 |
| `ApexApiClient` (Week 4 Part D typed client) | Fake `HttpMessageHandler` returning scripted responses | 200 + JSON body, 404, 500, timeout / caller cancellation, `application/problem+json` with `detail` and `errors` | R5 |

**What these mocks do not prove** (for the viva): the `ShipmentTrackingService` mocks prove that *if* the
repository reports "exists", or the unit of work throws `DuplicateKeyException`, the service answers 409 and
discards. They do **not** prove that SQL Server really throws on a duplicate, or that `ExistsAsync` really finds
the row. That is covered by the SQL Server integration test (§5).

**End-to-end API test:** `WebApplicationFactory<Program>` + a SQL Server test database (real migrations) + a
**test authentication handler** (a scheme that turns test headers into Courier or Admin claims). It covers:
create → upload scans → read back status and latest scan, plus one 403 ownership check.
*Why a test auth handler here, when `ApexPost.Api.Tests` already uses real JWTs:* the JWT validation itself is
already proven by `SecurityTests`. In the end-to-end test we want to exercise **authorization rules + database**,
without coupling it to token signing and lifetimes. This is the standard ASP.NET Core integration-testing pattern.

---

## 9. TDD plan (Task 4)

**Rule:** *"Shipments over 30 kg require a heavy-parcel surcharge."*

*Why this rule:*
* It **does not exist yet** (checked: the status rule "Delivered → InTransit is not allowed" and the
  "max scans per upload" limit already exist, so they could not be built test-first honestly).
* It has **sharp boundaries** (30.000 kg = no surcharge, 30.001 kg = surcharge, up to the 70 kg maximum), which
  suit a parameterized RED test.
* It needs a natural **testability improvement**: a surcharge policy behind an interface, injected by
  constructor, with the threshold and amount from options instead of hard-coded. That gives TDD-LOG.md a real
  "what I changed to make it testable, and why" story.
* It is small enough that each Red → Green → Refactor step is a reviewable commit.

Commits: `RED:` (failing tests only) → `GREEN:` (minimal code) → `REFACTOR:` (clean up, tests unchanged and green).
Every commit is made only after explicit approval.

---

## 10. BDD plan (Task 5)

| Feature | Level | Why this level |
|---|---|---|
| `ShipmentCreation.feature`: valid creation, duplicate tracking number, invalid weight (Scenario Outline with Examples at the 0.1 / 70 kg boundaries) | **Service level** (`ShipmentTrackingService` over the stateful in-memory store) | The scenarios describe *business rules*. HTTP status codes are already covered by the API tests. Service level runs in milliseconds, needs no database, and keeps the Gherkin in business language ("the shipment is rejected because…") instead of HTTP language. |
| `ScanSync.feature`: offline scans uploaded, duplicate scan skipped, invalid scan rejected, server unreachable keeps scans pending | **Service level** (`OutboxService` + server-side `ShipmentTrackingService` connected in-process by a test uploader) | Shows the whole business journey (phone outbox → server rules → statuses) without a network, which makes it deterministic and fast. |

Bindings: state is shared through **context injection** (a `ShipmentContext` POCO injected into step classes),
never static fields. `[BeforeScenario]` creates a fresh store, clock and services. `[AfterScenario]` disposes
them. Each feature has a `Background`, and at least one `Scenario Outline` with `Examples`.
*Why context injection:* Reqnroll creates a fresh context per scenario, so scenarios cannot leak into each
other, and parallel execution stays safe.

**Migration note:** there was no SpecFlow work in earlier weeks, so nothing needs migrating. Reqnroll is used
from the start.

---

## 11. AI-assisted test drafting (Task 6)

**Decision:** prompts are written by the author with Claude's help, **run in GitHub Copilot Chat** in Visual
Studio, and the raw Copilot output is pasted into `COPILOT-LOG.md`. Each generated test is then reviewed
(kept / changed / rejected, with the reason).

*Why:* the task assesses **Copilot** output and the human review of it. Presenting another tool's output as
Copilot's would make the log dishonest. Keeping the raw output separate from the reviewed version also makes the
review decisions visible (weak assertions, wrong expected values, copied implementation logic, missing edge
cases).

**Proof that generated tests are meaningful:** for at least two accepted tests we deliberately break the
production code (for example flip a boundary `>` to `>=`, or skip `DiscardChanges`), show the test failing, then
restore the code. The failing output is recorded in the log.
*Why:* a test that cannot fail is not a test. Mutation by hand is the cheapest proof.

---

## 12. What we will not test, and why

| Not tested | Why |
|---|---|
| MAUI pages, navigation, XAML bindings | Out of scope for this assignment. The courier logic was deliberately moved into `ApexPost.Courier.Core`, which *is* tested. |
| Browser / Swagger UI rendering | Out of scope (no front-end testing). The OpenAPI *documents* are already checked by `ApiVersioningTests`. |
| NSwag-generated `ApexPost.Client` code | Generated from the contract. Testing it would test NSwag. The contract itself is checked by the versioning tests. |
| Polly, EF Core, ASP.NET Core and Couchbase Lite internals | Third-party code with its own tests. We test *our configuration* of them (retry only transient errors, our mappings, our queries). |
| Performance and load (`database/perf`, `PERFORMANCE.md`) | Measured separately with execution plans. Timing assertions in unit tests are non-deterministic (see `ConcurrencyAndCachingTests`). |
| Real external services (production SQL, real partner endpoints, e-mail) | Forbidden by the constraints. Ports are faked or mocked. |
| Trivial code (DTO records, auto-properties, DI registration one-liners) | Low risk with no logic. Covered indirectly by the API and integration tests that build the real container (`ValidateOnBuild = true`). |
| 100 % coverage | Coverage is evidence, not a target. We aim for high coverage of the risk-map components, not of everything. |

---

## 13. How to run

```powershell
dotnet test ApexPost.slnx
```

No manual setup: SQL tests use `(localdb)\MSSQLLocalDB` by default (set `APEXPOST_TEST_SQL` to use another
server). Couchbase tests use temporary folders. All test configuration is in-memory and fictional. Details, plus
the run summary, are in the README "Tests" section.

---

## 14. Results & reflection

*To be completed in Task 7 after the full suite has run: passed / failed / skipped summary, optional coverage
comment, what the strategy caught, what we would change, and which test type gave the most confidence per minute.*
