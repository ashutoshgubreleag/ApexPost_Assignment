# TDD Log: Heavy-parcel surcharge (Week 5, Task 4)

Branch `week5-testing`. The cycle is visible in the Git history as three commits:

| Step | Commit | State of `dotnet test` |
|---|---|---|
| **RED** | `9ddb61b` RED: heavy-parcel surcharge tests … | 20 new tests **fail**, 604 existing tests pass |
| **GREEN** | `46935ce` GREEN: minimal heavy-parcel surcharge implementation | **624 passed**, 0 failed |
| **REFACTOR** | `REFACTOR: …` (the commit that adds this log) | **624 passed**, 0 failed, **no test file changed** |

```powershell
git log --oneline --grep "^RED:\|^GREEN:\|^REFACTOR:"
```

---

## 1. The requirement

> **"Shipments over 30 kg require a heavy-parcel surcharge."**

Made precise **before** writing code, because "over 30 kg" is ambiguous until the boundary is decided:

| Weight | Surcharge |
|---|---|
| 0.1 kg up to and including **30 kg** | 0.00 (30.000 kg itself pays nothing: "over" means strictly greater) |
| over 30 kg, up to and including **50 kg** | **15.00** |
| over 50 kg, up to and including **70 kg** | **25.00** |
| below 0.1 kg, or over 70 kg | not a valid shipment, so `ArgumentOutOfRangeException` |

Only shipments **booked on or after an effective date** pay it (a price change is never applied retroactively).
For an existing shipment the *booking time* counts, not "now". For a quote, "now" comes from the clock.

**Why this rule:** it did not exist in ApexPost. "Delivered → InTransit is refused" and the scan-batch limit
already existed, so they could not honestly be built test-first. It has sharp boundaries for parameterized tests,
and the effective date needs time, which forces a testable clock.

---

## 2. RED: the first failing tests

Files: `ApexPost.UnitTests/Tdd/HeavyParcelSurchargePolicyTests.cs` (18 cases) and
`ApexPost.UnitTests/Tdd/ShipmentChargesTests.cs` (2 cases).

The first test written was the boundary theory that pins down the meaning of "over 30 kg":

```csharp
[Theory]
[InlineData("0.1")]     // lightest allowed parcel
[InlineData("1")]
[InlineData("29.999")]
[InlineData("30")]      // exactly the threshold: "over 30 kg" means 30 itself pays nothing
public void SurchargeFor_WeightUpToThirtyKg_ReturnsZero(string weight)
{
    // Arrange
    var policy = CreatePolicy(AfterEffective);

    // Act
    var surcharge = policy.SurchargeFor(Kg(weight), AfterEffective);

    // Assert
    Assert.Equal(0m, surcharge);
}
```

It was followed by:
* the 15.00 band (30.001 / 40 / 50) and the 25.00 band (50.001 / 60 / 70);
* out-of-range weights (70.001 / 0.099 / 0 / −5) throwing `ArgumentOutOfRangeException` with `ParamName == "weightKg"`;
* the effective date: one tick before it pays 0, exactly at it pays the surcharge;
* `QuoteNow` with a `FakeTimeProvider` that is **advanced** across the effective date;
* a custom effective date coming from options;
* for the service, a **Moq** test that `GetChargesAsync` asks the policy with the shipment's *weight and booking time*
  (`Verify(p => p.SurchargeFor(45m, bookedAt), Times.Once)` and `QuoteNow` called `Times.Never`), and that an unknown
  shipment throws `EntityNotFoundException` without asking the policy.

**How RED was made to compile.** C# will not run a test that references types that don't exist yet, and a
compile error is not a *failing test*. The RED commit therefore adds **signatures only**: `IShipmentSurchargePolicy`,
`HeavyParcelSurchargeOptions`, `ShipmentChargesDto`, and a `HeavyParcelSurchargePolicy` /
`ShipmentTrackingService.GetChargesAsync` whose bodies throw `NotImplementedException`. That is the usual way to
show a genuine RED in a compiled language: the tests run and fail for the right reason (no behaviour yet).

RED run (abridged):

```
Failed!  - Failed:    20, Passed:   153, Skipped:     0, Total:   173 - ApexPost.UnitTests.dll
  ...HeavyParcelSurchargePolicyTests.SurchargeFor_WeightUpToThirtyKg_ReturnsZero(weight: "30") [FAIL]
  ...HeavyParcelSurchargePolicyTests.SurchargeFor_WeightOverThirtyUpToFiftyKg_ReturnsFifteen(weight: "30.001") [FAIL]
  ...HeavyParcelSurchargePolicyTests.QuoteNow_ClockMovesPastEffectiveDate_SurchargeStartsApplying [FAIL]
  ...ShipmentChargesTests.GetChargesAsync_HeavyShipment_ReturnsThePolicysSurchargeForItsWeightAndBookingTime [FAIL]
     System.NotImplementedException : The method or operation is not implemented.
Passed!  - Failed:     0, Passed:   157 - ApexPost.Api.Tests.dll
Passed!  - Failed:     0, Passed:    65 - ApexPost.IntegrationTests.dll
... (all other projects green)
```

Exactly the 20 new tests failed. Every existing test still passed, which proves the scaffolding broke nothing.

---

## 3. GREEN: the minimal implementation

The simplest code that turns every test green: literal numbers and an `if` chain.

```csharp
public decimal SurchargeFor(decimal weightKg, DateTimeOffset bookedAtUtc)
{
    if (weightKg < 0.1m || weightKg > 70m)
        throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, "Weight must be between 0.1 and 70 kg.");
    if (bookedAtUtc < _options.EffectiveFromUtc) return 0m;
    if (weightKg > 50m) return 25.00m;
    if (weightKg > 30m) return 15.00m;
    return 0m;
}

public decimal QuoteNow(decimal weightKg) => SurchargeFor(weightKg, _clock.GetUtcNow());
```

`ShipmentTrackingService.GetChargesAsync` loads the shipment (404 if unknown) and returns
`new ShipmentChargesDto(tn, weight, surcharges.SurchargeFor(weight, createdAt))`.

Result: **624 passed, 0 failed, 0 skipped.**

---

## 4. REFACTOR: what changed (tests untouched, still 624 green)

| Refactoring | Why |
|---|---|
| **Bands moved from literals into `HeavyParcelSurchargeOptions.Tiers`** (`SurchargeTier(OverKg, Amount)`), with the current bands as `DefaultTiers` | Prices change. A price change should be a configuration change, not a code change and a redeploy. |
| **`if` chain replaced by a table lookup** (bands sorted heaviest first; the first band the weight exceeds wins) | Adding a third band is now data, not another `if`. Ordering is done once in the constructor, not per call. |
| **Duplicated weight limits extracted** to `TrackingLimits.MinWeightKg/MaxWeightKg`, `IsWeightInRange`, `EnsureWeightInRange` and `WeightRangeMessage`; `ShipmentTrackingService.ValidateDetails` now uses them too | `0.1` and `70` were written out in the service, the domain and the new policy. Two copies of a rule drift apart. The message is built with the invariant culture, so it reads "0.1" on every machine. |
| **Options validated at start-up** (`AddOptions().Bind().Validate(o => o.IsValid()).ValidateOnStart()`): bands inside the weight range, positive amounts, distinct start weights | A typo in configuration fails the deployment immediately instead of charging customers wrongly. |
| **`Tiers` deliberately empty by default** (`EffectiveTiers` falls back to `DefaultTiers`) | The .NET configuration binder **appends** to a pre-filled list. Pre-filling it with defaults would have silently mixed default and configured bands. |

Proof that the refactor kept the behaviour: `git diff 46935ce -- '*Tests*'` is empty, and the suite is still
624 / 624.

---

## 5. Production code changed to make it testable, and why

| Change | Why it was needed for testing |
|---|---|
| **`TimeProvider` injected** into `HeavyParcelSurchargePolicy` (used by `QuoteNow`) instead of reading `DateTime.UtcNow` | "Is the surcharge in force *now*?" depends on time. With `DateTime.UtcNow` the test result would change on 1 December 2026. With `FakeTimeProvider` the test sets the clock one minute before the date, asserts 0, **advances** one minute, and asserts 15.00: deterministic on any day. |
| **Interface extracted: `IShipmentSurchargePolicy`** | Lets the service test replace the policy with a Moq mock and verify the *interaction* (asked with weight 45 and the booking time, never with "now") without re-testing the band arithmetic. Each class is tested for its own job. |
| **Constructor injection into `ShipmentTrackingService`** (new constructor parameter, registered in `AddApexPostServices`) | No `new HeavyParcelSurchargePolicy()` hidden inside the service, so tests choose the implementation. Side effect: the four places that build the service by hand (two in `ApexPost.Api.Tests`, two in `ApexPost.UnitTests`) now pass a policy. They were updated in the RED commit so that RED only failed the *new* tests. |
| **Settings through `IOptions<HeavyParcelSurchargeOptions>`** instead of constants | Tests construct the policy with `Options.Create(new HeavyParcelSurchargeOptions { EffectiveFromUtc = … })`, so they never depend on the production default date. |
| **Booking time passed in explicitly** (`SurchargeFor(weight, bookedAtUtc)`) rather than read inside | Pure input → output: the band and date rules are tested without any clock at all. Only `QuoteNow` touches the clock. |

---

## 6. What I would do next (not in this cycle)

* Surface `heavyParcelSurcharge` in the API. That is a contract change, so it belongs in a new API version or an
  additive v2 field with updated `contracts/v2.json` and the NSwag client (see `VERSIONING.md`), driven by its own
  failing API test first.
* Add the surcharge to the counter price quote in `ShippingService` if the business wants it charged at the counter.
