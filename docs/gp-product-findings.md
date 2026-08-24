# Product findings — issues filed

Defects in product code found while reviewing every commit on the default branch of all GP repositories for
2026-01-01..2026-08-22. These need fixing in the owning repository and are independent of whether a rule is ever
written for them. The rule candidates they motivated live in [gp-rule-backlog.md](gp-rule-backlog.md).

All repositories listed are private, so the issue bodies describe the defects in full.

| # | Repo | Issue | What | Live today? |
| --- | --- | --- | --- | --- |
| 1 | GP.Odra | [#1074](https://github.com/GrupaPracuj/GP.Odra/issues/1074) | 21 purge commands whose `DELETE` has no `WHERE` and would clear the whole table | **No** — all 21 sit behind a commented-out caller |
| 2 | GP.Odra | [#1075](https://github.com/GrupaPracuj/GP.Odra/issues/1075) | `AnonymizeUsersAssignedToCompanyExclusively` leaves first name, last name and position in place | Yes |
| 3 | GP.Odra | [#1076](https://github.com/GrupaPracuj/GP.Odra/issues/1076) | `Product.RestrictionLevel` silently participates in record-struct equality on the credits path | Yes (impact depends on provenance) |
| 4 | GP.Wierzbiak | [#96](https://github.com/GrupaPracuj/GP.Wierzbiak/issues/96) | `AllocationHistoryHandler` publishes the change event before persisting the change | Yes |
| 5 | GP.Wierzbiak | [#97](https://github.com/GrupaPracuj/GP.Wierzbiak/issues/97) | `GetChangesByAllocationId` always returns empty `Products` — `splitOn` marker leaves `Id` unmapped | Yes (reasoned, not executed) |
| 6 | GP.Wierzbiak | [#98](https://github.com/GrupaPracuj/GP.Wierzbiak/issues/98) | Duplicate products in the request body cause a 500; `AdhibitAsync` can double-grant | Yes |
| 7 | GP.Kaczawa | [#130](https://github.com/GrupaPracuj/GP.Kaczawa/issues/130) | Audit trace stamped with `TimeProvider.System` instead of the injected `TimeProvider` | Yes |
| 8 | GP.Plunger | [#623](https://github.com/GrupaPracuj/GP.Plunger/issues/623) | SOQL injection — `customerNIP` interpolated into Salesforce queries | Yes |

## How to read the "live today" column

It is deliberately separate from severity. Issue 1 is the worst defect of the eight and the least urgent: the
entire offer-clearing subtree of the company purge would wipe 21 tables globally, but every one of those commands
hangs off a single commented-out line in `ClearCompanies.cs:40`. It needs fixing before that line is uncommented,
not before the next release.

Issues 3 and 5 are the two where I could not fully establish runtime impact from the source. Both bodies say so
explicitly rather than asserting a failure — 3 depends on which code paths supply the two sides of the comparison,
5 has no test over the path and I did not run it.

## Checked and deliberately not filed

Recorded so nobody re-investigates them:

- **`ReservedCredit.BundleItemsDetails` NRE, GP.Odra.** Four event builders dereference it without a null check,
  and it is only ever assigned in `GetReservedCreditQuery.QuerySingle`. Not a defect: that query is the sole
  materialisation path for the type, `ReadAsync<T>` returns an empty sequence rather than null, and
  `CreditsModule.FilterSuitable4BookingOperations` handles a null credit explicitly before any builder runs.
- **`credit?.BundleItemsDetails = bundleItems`, GP.Odra.** Looks like invalid C#; it is null-conditional
  assignment, a C# 14 feature, and does exactly what is intended.
- **`AddSingleton<IAllocationHistoryRepository, …>`, GP.Wierzbiak.** Looked like a captive dependency among
  `AddScoped` neighbours; its only dependency `IDatabaseConnectionFactory` is also a singleton, so it is fine.
- **`GP.Scylla` `ToDictionary` over query parameters.** Real, but already fixed in `d8bc4f8f5`.
- **Schema and table names interpolated into SQL** across `GP.Wierzbiak`, `GP.Kaczawa`, `GP.Scylla`, `GP.Shrek`,
  `GP.Wilga`, `RichieRich`, `GP.Abakus` (~127 sites). All interpolate constants, `nameof`, or `static readonly`
  fields initialised with literals — not injectable. Only `GP.Plunger` interpolates caller-supplied data.
