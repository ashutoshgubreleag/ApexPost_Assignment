# ApexPost Counter Platform

ApexPost is a national postal, shipping and retail services provider. This is the counter platform
its branches use: staff sell retail goods, letters and parcels, money orders and identity
verification from one counter. It is an ASP.NET Core MVC app on .NET 10 / C# 14.

**Week 3** moves the system of record to **SQL Server (`ApexPostDb`) through EF Core 10** and adds
**`ApexPost.CounterTerminal`**, an offline-first console terminal on **Couchbase Lite 4.1** that keeps
selling when the network is down and syncs when it comes back.

* Architecture, ER diagram, sync design, ADRs, SOLID: **[DESIGN.md](DESIGN.md)**
* Indexes, execution plans, before/after numbers: **[PERFORMANCE.md](PERFORMANCE.md)**
* Offline conflict policies: **[CONFLICTS.md](CONFLICTS.md)**
* Demo script: **[docs/DEMO.md](docs/DEMO.md)** · Week 2 design (middleware, filters, auth): [docs/DESIGN-week2.md](docs/DESIGN-week2.md)

![Branch dashboard](docs/screenshots/dashboard.png)

## Solution structure

| Project | Role (see DESIGN.md §2) |
| --- | --- |
| `ApexPost.Models` | **Domain**: rich aggregates with behaviour and invariants, value objects, domain exceptions |
| `ApexPost.Services` | **Application**: use-case services, persistence ports (`IUnitOfWork`, repositories, query services), DTOs, offline-sale processing |
| `ApexPost.Database` | **Infrastructure (persistence model)**: `ApexPostDbContext`, Fluent API mappings, migrations, seed data, stored procedures |
| `ApexPost.Repositories` | **Infrastructure (data access)**: EF Core repositories, unit of work, query services, SQL audit store |
| `ApexPost.Contracts` | Wire contract for `/api/sync`, shared by server and terminal |
| `ApexPost.Utilities` | Middleware (incl. device authentication), filters, exception mapping |
| `ApexPost.MVC` | **Web**: composition root, controllers, Razor views, sync API |
| `ApexPost.CounterTerminal` | Offline counter terminal (Couchbase Lite) |
| `ApexPost.Tests` | Unit tests (fakes) and WebApplicationFactory tests |
| `ApexPost.CounterTerminal.Tests` | Terminal tests on a real Couchbase Lite database |
| `ApexPost.IntegrationTests` | SQL Server tests on disposable, migrated databases |
| `database/` | `schema.sql` (idempotent migration script) and `perf/` (volume generator, workloads, results) |
| `scripts/` | `run-perf.ps1` (before/after plans), `verify-week3.ps1` (build + all tests + perf) |

Flow: **Controller → Service → Repository/Query service → EF Core → SQL Server**. Controllers get DTOs only.

## Prerequisites

* .NET 10 SDK
* SQL Server 2019 or later (developer/evaluation edition or LocalDB). Tested with SQL Server 2025.
* Optional: SSMS or Azure Data Studio to open the `.sqlplan` files and browse the database
* A trusted HTTPS development certificate: `dotnet dev-certs https --trust` (cookies are `Secure`;
  the terminal calls the server over HTTPS)
* Couchbase Lite needs nothing installed: it is the `Couchbase.Lite` NuGet package (4.1.2)

## 1. Create the database

Secrets are never committed. Store the connection string with user-secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:ApexPostDb" "Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True" --project .\ApexPost.MVC
# LocalDB instead:  "Server=(localdb)\MSSQLLocalDB;Database=ApexPostDb;Trusted_Connection=True"
```

Then apply the migrations, either way:

```powershell
# a) EF Core tools (dotnet tool install --global dotnet-ef)
$env:APEXPOST_DESIGN_CONNECTION = "Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update --project .\ApexPost.Database

# b) or run the idempotent script in SSMS / sqlcmd (safe to run more than once)
sqlcmd -S . -E -C -Q "IF DB_ID('ApexPostDb') IS NULL CREATE DATABASE ApexPostDb"
sqlcmd -S . -E -C -d ApexPostDb -i .\database\schema.sql
```

In **Development** the web app also applies pending migrations at start-up and seeds 60 days of
fictional history (`Database:MigrateOnStartup`, `Database:SeedHistory` in
`appsettings.Development.json`). `EnsureCreated` is never used.

The migrations create the 20 tables, all constraints, seed data, the five reporting indexes, the
three stored procedures (`usp_DailyBranchSales`, `usp_LowStockReport`, `usp_AdjustStock`) and the
least-privilege role `apexpost_app`. For a non-development deployment, create a login for the app
and add it to that role instead of using an administrator connection.

## 2. Configure keys (user-secrets)

```powershell
# Courier partner API key (integration endpoint)
dotnet user-secrets set "Integration:Partners:swiftcourier" "<a-long-random-key>" --project .\ApexPost.MVC

# Counter terminal device key: the same value on the server and on the terminal
dotnet user-secrets set "Sync:Devices:TERM-BR001-01:Key" "<a-long-random-device-key>" --project .\ApexPost.MVC
dotnet user-secrets set "Terminal:DeviceKey"            "<a-long-random-device-key>" --project .\ApexPost.CounterTerminal
```

`TERM-BR001-01` is registered for branch **BR-001** in `appsettings.json` (the key itself is empty
there on purpose). Environment variables work too: `Sync__Devices__TERM-BR001-01__Key`,
`APEXPOST_Terminal__DeviceKey`.

## 3. Run the web app

```powershell
dotnet run --project .\ApexPost.MVC --launch-profile https
```

Open **https://localhost:7052**. All demo accounts use the password **`ApexPost123!`** (stored only as
PBKDF2 hashes):

| Email | Role | Use it to show |
| --- | --- | --- |
| `clerk.br001a@apexpost.local` | Counter Clerk, BR-001 | Sales, shipping, money orders, ID checks |
| `manager.br001@apexpost.local` | Branch Manager, BR-001 | Void/refund, stock adjustments (stored procedure), sync conflicts report |
| `auditor@apexpost.local` | Auditor | All-branch reports, money order / ID audit, audit log |
| `admin@apexpost.local` | Administrator | Branches, staff, **price revision** (creates offline price conflicts) |
| `clerk.br006a@apexpost.local` | Counter Clerk, BR-006 (Closed) | A closed branch cannot trade |
| `former.clerk@apexpost.local` | Inactive | Inactive staff cannot sign in |

## 4. Run the counter terminal

In a second terminal window, with the web app running:

```powershell
dotnet run --project .\ApexPost.CounterTerminal
```

```
> sync                         pull catalogue, prices and BR-001 stock
> categories
> catalog Packaging            SQL++ query, sorted by name
> sell PKG-001x2 PKG-002x1 pay=Card
> offline                      simulate pulling the cable
> sell PKG-001x1               still works: saved to the local outbox
> mo 250 Ada Bo                low-value money order: allowed offline
> mo 1500 Ada Bo               refused: high-value money orders need the network
> status                       offline, 2 pending
> pending                      pending sales and their attempts
> online                       reconnect: pushes the outbox, pulls prices/stock
> today                        today's sales from the local database
```

Settings are in `ApexPost.CounterTerminal/appsettings.json` (`Terminal` section): device id,
branch, counter, clerk, `DataDirectory` (where the Couchbase Lite database lives, default
`terminal-data`), server URL, sync interval and backoff. Any setting can be overridden on the
command line, for example `--Terminal:ForceOffline=true`. A scripted run:
`dotnet run --project .\ApexPost.CounterTerminal -- --run "sync;sell PKG-001x1;status"`.

The full walkthrough (offline sale, reconnect, price conflict, duplicate push) is in [docs/DEMO.md](docs/DEMO.md).

## Sync API

| Endpoint | Purpose |
| --- | --- |
| `POST /api/sync/transactions` | Push up to 50 offline sales. Idempotent per `(device, localTransactionId)` |
| `GET /api/sync/branch-snapshot?since=...` | Catalogue, prices and the device's branch stock (delta since the last pull) |

Headers `X-Device-Id` and `X-Device-Key`; HTTPS only; rate limited per device (429); errors are
Problem Details (400/401/409/429). See DESIGN.md §6.

## Tests

```powershell
dotnet build .\ApexPost.slnx                    # zero warnings (TreatWarningsAsErrors)
dotnet test  .\ApexPost.Tests                   # 134 unit, web, over-posting and terminal-to-server tests, no database needed
dotnet test  .\ApexPost.CounterTerminal.Tests   # 31 tests on a real Couchbase Lite database
$env:APEXPOST_TEST_SQL = "Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True"
dotnet test  .\ApexPost.IntegrationTests        # 29 SQL Server tests (creates and drops ApexPostTest_* databases)
```

Or everything, including the performance run, with logs in `artifacts\verify`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-week3.ps1
```

DESIGN.md §10 lists what each project proves.

## Configuration

| Section | Purpose |
| --- | --- |
| `ConnectionStrings:ApexPostDb` | SQL Server (user-secrets) |
| `Database` | `MigrateOnStartup`, `SeedHistory`, `MaxRetryCount` (transient-fault retries), `LogSqlCommands` |
| `Sync` | Registered devices (`Devices:{id}:BranchCode`, key in user-secrets), `MaxSalesPerPush`, `RequestsPerMinute` |
| `BusinessRules` | Money order thresholds and fees, low-stock threshold, `ConcurrencyRetryAttempts`, `OfflinePriceVarianceTolerance` |
| `Courier`, `MaintenanceMode`, `IpRestriction`, `RateLimiting`, `Cors`, `Performance` | As in Week 2 (docs/DESIGN-week2.md) |

## Security notes

* No secrets in source control: connection string, partner keys and device keys come from
  user-secrets or environment variables.
* No string-concatenated SQL: EF LINQ, `FormattableString` raw SQL (`SqlQuery`, `ExecuteSqlAsync`)
  and parameterized SQL++ only.
* Identity document numbers are masked everywhere: only the last four characters reach SQL Server;
  the audit log whitelists fields; the terminal never stores identity data.
* All data is fictional.

## Git workflow

Week 3 is developed on `feature_Week3` with one commit per layer (domain, application, database,
repositories, sync contracts, web, tests, terminal, docs) and merged through a pull request.
`bin/`, `obj/`, `terminal-data/`, `artifacts/` and user-secrets are never committed.
