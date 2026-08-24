# GP rule backlog

Candidates found by reviewing every commit on the default branch of all 132 active repositories in
`C:\Git\runn for all\repos` between **2026-01-01 and 2026-08-22** (14 378 commits, 445 387 added C# lines,
223 549 of them outside test paths).

Read `AGENTS.md` first — it owns the conventions.

### How these were found, and what that bounds

Two passes, and the second one is much thinner than it sounds.

The mechanical pass covered all 223 549 added production lines against a fixed set of ~35 patterns. That is
complete for those patterns and blind to everything else.

The reading pass is an in-progress line-by-line sweep of all 95 repositories that have production changes,
worked in descending order of size. Progress so far: `GP.Wierzbiak` ~1 600 of 6 054 diff lines, `GP.Kaczawa` ~270
of 6 847, `GP.Odra` ~1 500 of 7 855 added-only lines, `GP.Yoda` only its final two weeks. Everything else has been
seen by the pattern scan alone.

That ratio is the main thing to take from this document. Every candidate from GP0122 down came out of the reading,
none out of the scanning, and the reading has so far touched a few percent of four repositories out of 132. The
yield per line read is roughly two orders of magnitude above the yield per line scanned, which is the argument for
continuing.

Several candidates rest on a single confirmed defect. That is deliberate: one real bug is enough reason to try to
prevent the next one, and waiting for a second occurrence means shipping the rule after it has already cost twice.

### Status of the rules this review previously shipped

GP0115–GP0118 were committed in `f1c6cbd89`. **`996b7609b` then removed GP0116, GP0117 and GP0118**, keeping only
GP0115, on the grounds that they were "cancellation and SQL heuristics that cannot avoid legitimate findings
without stronger semantic context".

That sets the bar for everything below, so read it as a constraint rather than trivia: a candidate that decides by
text shape rather than by semantics will be rejected, however good its motivating example is. Where a candidate
here is still partly heuristic, that is called out under its own heading.

`GP0119` is taken by `MigrationDdlShouldUseFluentMigratorExpressions` (`47dc077f3`), so **the next free id is GP0120**. `GpSqlText.cs`, the shared SQL text reader, was deleted along with
GP0117/GP0118; it is recoverable from `f1c6cbd89` if a future rule needs it.

Each candidate below states what the evidence is, why the rules we already have cannot see it, and how to detect
it. Confidence is about *whether the rule is worth shipping*, not about whether the underlying defect is real.

---

## GP0120 — Query text built by interpolation and passed to a company query API

**Confidence: high. Value: high (security).**

### Does it clear the bar that removed GP0116–GP0118?

Partly, and the difference is worth stating before anyone starts. The decision it makes is semantic, not textual:
it asks the semantic model what each interpolation hole *is* — a constant, a `nameof`, a literal-initialised
`static readonly`, or something else — and only the last category is reported. That is a symbol-level question with
a definite answer, unlike "does this timestamp column happen to be unique" or "is this opt-out legitimate", which
is what sank GP0117 and GP0116.

What remains heuristic is the *trigger*: deciding that a string is query-shaped from its keywords. Get that wrong
and the rule fires on prose. Mitigation is to require a real query shape — `SELECT` … `FROM`, or `INSERT INTO`,
not a bare keyword — and to accept false negatives on unusual query forms rather than widening it. If reviewers
are still not satisfied, the fallback is to require a known sink after all, which shrinks coverage to the Salesforce
service but makes the trigger exact.

### Evidence

`GP.Plunger/src/GP.Plunger.Consumer/PracujClub/IPracujClubMembershipService.cs`, three sites — lines 145, 242, 279:

```csharp
var accounts = await _salesforceService.GetByQuery(
    $"SELECT Id,FLSA_EXTRANET_ID__C FROM Account WHERE FLSA_NIP__C = '{customerNIP}'");
```

`customerNIP` reaches a **quoted literal inside a SOQL string**. The taint path is complete and short:

| step | code |
| --- | --- |
| message field | `PracujClubSavingsConsumer.cs:23` → `message.CustomerNIP` |
| | `PracujClubBenefitConsumer.cs:30` → `message.CustomerNip` |
| public method | `UpdateSavings(string customerNIP, …)`, `MarkQuarterlyDiscountUsed(string customerNIP)` |
| sink | `ISalesforceService.GetByQuery(string query)` — `GP.Plunger.Common/Salesforce/SalesforceService.cs:32-33` |

A value containing `'` breaks the query outright; `' OR Name != '` turns it into a data-exfiltration primitive.

### Why nothing catches it today

`S2077` ("SQL queries should not be dynamically formatted") resolves its sinks from a **hardcoded `KnownType`
list** — see `analyzers/src/SonarAnalyzer.Core/Rules/ExecutingSqlQueriesBase.cs:33-52`: ADO.NET command/adapter
types, `Microsoft_EntityFrameworkCore_RawSqlString`, `Dapper_CommandDefinition`. A company-internal
`ISalesforceService.GetByQuery(string)` is not on that list and never will be. This is the general shape of what
escapes us: the upstream rule knows the framework's sinks, not ours.

### Detection

Trigger on `InterpolatedStringExpression` (and `+` concatenation producing a string).

1. The literal portions, concatenated, look like a query — reuse `GpSqlText.LooksLikeSql`, but extend its keyword
   set: SOQL has no `INSERT INTO`, so accept a bare `SELECT … FROM`.
2. At least one interpolation hole is **not** a compile-time-safe value.
3. Report the interpolated expression.

**Do not require the sink to be known.** Requiring a sink is what makes `S2077` blind here. Query-shaped text with
a non-constant hole is worth reporting wherever it is built.

### FP guards — this is the whole game

The dominant safe pattern in these repos interpolates schema and table identifiers, ~130 occurrences across
`GP.Wierzbiak`, `GP.Kaczawa`, `GP.Scylla`, `GP.Shrek`, `GP.Wilga`, `RichieRich`, `GP.Abakus`, `extranet2`:

```csharp
$"FROM [{DbSchema.Name}].[{ChangeProductTableName}]"
$"INSERT INTO {nameof(SharedGroup)} ("
```

**Critical implementation detail: `DbSchema.Name` is `static readonly string`, not `const`.** Verified in
`GP.Wierzbiak`, `GP.Kaczawa` (`public static readonly string Name = "dbo";`), `GP.Scylla` (`Dbo`), `GP.Shrek`
(`Default` — `const` in one place, `static readonly` in another). So `SemanticModel.GetConstantValue` on the hole
returns nothing and a naive "non-constant ⇒ report" check false-positives on **every one of those ~130 lines**.

Treat a hole as safe when it is any of:

- a `const`;
- a `nameof(...)` expression;
- a `static readonly` field whose initializer is a string literal (follow the declaring syntax);
- a `static` property whose getter returns a string literal.

Report only holes that are none of those — parameters, locals derived from parameters, message fields, properties
of DTOs. That split puts all ~130 known-safe lines on the safe side and all 3 Plunger lines on the reported side.

### Known FN

Concatenation across statements (`var sql = "SELECT …"; sql += userInput;`) needs dataflow and is out of scope for
a syntactic rule. Document it with a `// FN:` comment.

---

## GP0121 — `WITH(NOLOCK)` inside a view definition

**Confidence: medium-high. Value: medium.**

### Evidence

59 `NOLOCK` occurrences in added production lines this year. The subset that matters is the one inside
`CREATE VIEW`:

| repo | file | views |
| --- | --- | --- |
| `GP.Wilga` | `Migration0130_DataLakeViews.cs` | 4 `CREATE VIEW`, 4 `NOLOCK` — one per view |
| `GP.Bobr` | `Migration072_UpdateJobPositionsGroupView.cs` | 4 |
| `GP.Bobr` | `Migration073_UpdateJobPositionsSetView.cs` | 4 |
| `GP.Bobr` | `Migration067`, `Migration068`, `Migration069` | 2 each |
| `GP.Wierzbiak` | `Migration0230.cs` | 3 |

A hint inside a view is invisible and non-overridable at every call site: a consumer selecting from
`vw_dbo_Orders` gets dirty reads, and cannot see or opt out of that from its own query. The same hint written
directly in a query is at least local and reviewable — which is why the rule should be scoped to views only.

### Why nothing catches it today

No `S` rule covers table hints. No GP rule does either. `GP0035` is about preserving the Juno connection and
transaction context, not isolation hints.

### Detection

String literal → `LooksLikeSql` → contains `CREATE VIEW` or `CREATE OR ALTER VIEW` → contains `NOLOCK` or
`READUNCOMMITTED`. Report the literal. Reuse `GpSqlText`; add a `HasViewDefinition` helper.

### Caveat before shipping

This looks like an established team pattern for dictionary/data-lake views, so it will flood existing code and
somebody has to agree it is worth fixing. `AGENTS.md` is explicit that a rule which floods a codebase with
true-but-low-value findings is not worth shipping. **Get a decision before implementing.** Options: ship it,
ship it scoped to non-`DataLake` schemas, or drop it.

---

## GP0122 — A publish that precedes the write it announces

**Confidence: high. Value: high. One confirmed instance.**

### Evidence

`GP.Wierzbiak/src/GP.Wierzbiak.Api/AllocationHistory/AllocationHistoryHandler.cs`, `HandleAsync` — current line
numbers, not just the diff:

```
line 45   var alreadyProcessed = await _repository.ExistsChangeBySnapshotId(status.AllocationSwapId);
line 81   await _eventStream.Publish(@event);              // announce
line 96   await _repository.InsertChangeWithProducts(...)  // ...then write, once per change, in a loop
```

The event says a change was registered before anything is stored. If the insert throws — or the process dies in
between — consumers acted on a change that `GetChangesByAllocationId` will never return. The loop makes it worse:
each iteration is its own connection and transaction, so a failure halfway leaves part of the history written and
the event already gone.

The idempotency guard at line 45 does not help. It checks whether a *change row* exists, and on a retry after a
failed insert there is none, so the handler republishes and tries again.

### Why nothing catches it today

`GP0048` ("A database commit and a message publish should not be a dual write") is **directional**. Its
implementation collects commit sites and publish sites in the analysed method, builds the CFG, and reports a
publish only when `commitSites.Any(x => CanReach(x, publishSite))` — commit *then* publish. The reverse order,
which is the more damaging one, is not reported at all.

There is a second gap in the same rule: `CommitMethods` is the literal set
`{SaveChanges, SaveChangesAsync, Commit, CommitAsync}`, matched against invocations **inside the analysed method**.
Here the write is `_repository.InsertChangeWithProducts(...)`, which opens its own transaction and commits
internally, so even the commit→publish direction would be invisible.

### Detection

Both fixes reuse machinery `GP0048` already has:

1. **Make it bidirectional.** Also report when a publish site can reach a commit site. Use a distinct message —
   the failure mode and the fix differ ("you announced a change that may not exist" vs "you changed data and may
   not have told anyone").
2. **See through one level of indirection.** Treat a call as a commit when the invoked method's own body — resolved
   within the compilation, following interface members to their implementers — contains a commit. One level is
   enough for the repository pattern used here and keeps the analysis bounded.

Step 1 alone catches this instance and is much the cheaper half; step 2 is worth doing separately.

---

## GP0123 — A static clock used where an injected clock is available

**Confidence: high. Value: medium-high. One confirmed instance.**

### Evidence

`GP.Kaczawa/src/GP.Kaczawa.Broker/Wallets/Usage/UseReservationsConsumer.cs` injects `TimeProvider` and then
bypasses it for the audit timestamp, inside the same class:

```csharp
private readonly TimeProvider _timeProvider;          // injected, used in Validate():
    ... message.Payload.UsageDateUtc < _timeProvider.GetUtcNow().UtcDateTime.AddDays(MaxBackPeriodInDays)

// ...and then, in Consume():
var auditTrace = new Core.AuditTrace(..., TimeProvider.System.GetUtcNow().UtcDateTime);
```

The injection exists so the clock can be controlled. Using `TimeProvider.System` for the audit stamp defeats it:
a test that fixes the clock still writes a real timestamp, and one logical operation ends up carrying two
timestamps from two different clocks.

I swept `GP.Kaczawa`, `GP.Wierzbiak`, `GP.Odra`, `GP.Yoda`, `GP.Shrek`, `GP.Scylla`, `GP.Bobr` and `GP.Gleipnir`
for files that both inject a clock abstraction and call a static one. This is the only real violation:
`GP.Gleipnir/src/GP.Gleipnir/AppTimeProvider.cs` also matches, but it *is* the abstraction, and the third match is
a test accessor. So: one instance, which is the whole point of the rule — it is the kind of thing that appears once
per service and is invisible in review.

### Why nothing catches it today

No `S` rule and no GP rule covers it. `GP0017` and `GP0018` police the naming and the `DateTimeKind` of date
members, not which clock produced the value.

### Detection

This is the same shape as `GP0027`, which is shipped and accepted: an abstraction is available in scope and the
code used a static escape hatch instead. Nothing heuristic about it.

1. The containing type has a clock available: a field, a primary-constructor parameter or a constructor parameter
   of type `System.TimeProvider`, or of a project abstraction (`ISystemClock`, `IClock`, `IDateTimeProvider` —
   make this list a rule parameter, the way `GP0048` parameterises `outboxTypes`).
2. The method body reads a static clock: `TimeProvider.System`, `DateTime.Now/UtcNow/Today`,
   `DateTimeOffset.Now/UtcNow`, `GP.Juno.Abstractions.SystemTime.UtcNow()`/`OffsetUtcNow()`.
3. Report the static read.

Exclusions: the type that *implements* the abstraction (it has to call the static clock somewhere), and types
whose name ends in `TimeProvider`/`Clock`. `scope: Main` keeps test doubles out.

---

## GP0124 — A row-limiting query with no ORDER BY at all

**Confidence: high. Value: medium. One confirmed instance.**

### Evidence

`GP.Odra/src/GP.Odra.Adapter/Logic/Credits/GetReservedCreditQuery.cs` — a correlated subquery that picks one of
several matching rows arbitrarily:

```sql
(SELECT TOP 1 rc2.reservCreditID
 FROM schOrders.tReservedCredits rc2
 WHERE rc2.orderDetailID = rc.orderDetailID
   AND rc2.isBundleItem = 0
   AND rc2.orderDetailID IN (SELECT orderDetailID FROM schOrders.tReservedCredits WHERE isBundleItem = 1)
) AS BundleReservedCreditId
```

Nothing constrains the result to one row, and there is no `ORDER BY`, so which `reservCreditID` becomes
`BundleReservedCreditId` depends on the plan. This is a credits/billing path.

### Why this is not the rule that was removed

`GP0118` was removed for guessing whether a timestamp column is unique. **This rule guesses nothing**: it fires
only when a row limiter is present and there is no `ORDER BY` in that query at all. "Rows returned are undefined"
is then not an inference about the schema, it is what T-SQL specifies. I deliberately left this case out of
`GP0118` (its test corpus lists `SELECT TOP (1) … WHERE …` as compliant, commented "a different problem, and not
this rule's") — it is that different problem.

### Detection

String literal → looks like SQL → has a row limiter (`TOP n`, `FETCH NEXT`, `OFFSET`) → the same statement has no
`ORDER BY`. `GpSqlText` from `f1c6cbd89` already has `HasRowLimiter` and `OrderByColumns`; recovering it gives both
predicates for free.

The one real subtlety is scoping the check to the statement the limiter belongs to, since a literal can hold
several statements and, as here, nested subqueries. Simplest safe version: only analyse literals containing exactly
one `SELECT`, and accept the false negatives on multi-statement literals rather than mis-pairing a limiter with
another statement's `ORDER BY`. Note that the instance above would be **missed** by that simplification, because
its literal holds several SELECTs — so if the rule is meant to catch this evidence, the limiter/ORDER BY pairing
has to be per-`SELECT`, which means tracking parenthesis depth. Decide which of the two you want before starting.

---

## GP0125 — Dapper `splitOn` marker that leaves the mapped type's key unmapped

**Confidence: medium-high, reasoned but not executed. Value: high — it silently empties a response.**

### Evidence

`GP.Wierzbiak/src/GP.Wierzbiak.Api/AllocationHistory/Db/Queries/GetChangesByAllocationIdQuery.cs`:

```sql
cp.Id        AS ProductId,          -- the split marker
cp.ChangeId, cp.ProductType,
cp.ProductId AS ProductProductId,   -- the real product id
...
```
```csharp
splitOn: "ProductId"
...
if (product != null && product.Id != 0) changeEntry.Products.Add(product);
```

`ChangeProductReadModel` has both `Id` (int) and `ProductId` (string). The second object starts at the column named
`ProductId`, and that segment contains no column named `Id`, so `product.Id` keeps its default `0` — which makes
the `product.Id != 0` guard permanently false and `Products` permanently empty. Separately,
`ProductProductId` matches no property at all, so the real product id is dropped.

The guard being written as `product.Id != 0` says the author expected `Id` to be populated. There is no test over
this path — it is the only `splitOn` in the repository and nothing in `src` covers
`GetChangesByAllocationId`/`ChangeWithProducts` — so I could not confirm it by running anything. Verify before
fixing, but the alias arrangement cannot populate `Id`.

### Detection

Narrow and mechanical: for a Dapper multi-mapping call with a literal `splitOn`, take the mapped type of the
segment and check that the columns from the marker onward cover its properties — in particular that a property
named `Id` has a matching column when the type has one. Report a selected alias matching no property on any mapped
type as well; that is the `ProductProductId` half and is the same check `GP0117` did, minus the cross-statement
guessing that got `GP0117` removed.

Only handle a literal `splitOn` and literal SQL. Anything computed, skip.

---

## GP0126 — `ToDictionary` over a caller-supplied collection

**Confidence: medium. Value: medium. Two instances, one of them already cost a fix.**

### Evidence

1. `GP.Wierzbiak/src/GP.Wierzbiak.Api/Services/ComplimentarySwapServiceWriter.cs`, `PostProductsAsync`:
   ```csharp
   var incomingModels = products.Select(p => new ComplimentarySwapProductReadModel { ... })
                                .ToDictionary(m => m.AllocatedProductId);
   ```
   `products` is the HTTP request body and `AllocatedProductId` is derived from `ProductId` + `Parameters`. Two
   entries for the same product with the same parameters therefore collide, and `ToDictionary` throws
   `ArgumentException` — an unhandled 500 driven entirely by request content.
2. `GP.Scylla`, commit `d8bc4f8f5`: `AsQueryParams().ToDictionary(x => x.Key, x => x.Value)` over query parameters
   where array parameters legitimately repeat a key. Already fixed by dropping the dictionary.

### Why it is hard, and what to do about it

"This sequence can contain duplicate keys" is undecidable in general, which is why I first recorded this as an
idea rather than a candidate. The tractable version restricts the source: report `.ToDictionary(...)` whose source
chain originates in a parameter of the enclosing method (directly, or through `Select`/`Where`), and which has no
preceding `Distinct`/`DistinctBy`/`GroupBy`. That is a local dataflow question with a definite answer, not a guess
about the data.

It will still flag call sites where the caller genuinely guarantees uniqueness, and there is no way for the
analyzer to see that guarantee. Weigh that before shipping; the honest framing is that `ToDictionary` on data you
did not construct is an unstated precondition, and the fix (`ToLookup`, `GroupBy`, or an explicit duplicate check
returning 422) is cheap.

---

## GP0127 — DELETE or UPDATE with no WHERE clause

**Confidence: high. Value: high. 21 confirmed instances in one directory, all currently dormant.**

Do this one first. It is the cheapest candidate to implement and the only one where the evidence is a systemic
defect rather than a single slip.

### Evidence

`GP.Odra/src/GP.Odra.Internal/Companies/Purging/DataAccess/` contains 47 table-clearing commands. **21 of them**
write `SELECT` where `FROM` belongs, which splits the literal into two statements and leaves the `DELETE`
unqualified:

```sql
DELETE schOffers.tOffers              -- a complete statement. No WHERE. Whole table.
SELECT * FROM schOffers.tOffers o
INNER JOIN schOffers.tCommonOffers co ON o.commonOfferID = co.commonOfferID
WHERE co.companyID = @companyId       -- this WHERE belongs to the SELECT
```

The other **26** get it right, and the contrast is what proves this is a typo rather than a design:

```sql
DELETE schOffers.tCommonOfferAddons
FROM   schOffers.tCommonOfferAddons coa   -- FROM, so the whole thing is ONE statement
INNER JOIN schOffers.tCommonOffers co ON coa.commonOfferId = co.commonOfferID
WHERE co.companyID = @companyId
```

The sharpest pair sits on the same target table: `ClearOffersCategoriesByReservedCreditId.cs` deletes from
`schOffers.tOffersCategories` correctly with `FROM`, while `ClearOffersCategoriesByCommonOfferId.cs` deletes from
the same table with `SELECT` and would wipe it.

The 21: `ClearCommonOffersInheritance`, `ClearCommonOffersProfeo`, `ClearOfferLocations`, `ClearOfferRellocation`,
`ClearOffers`, `ClearOffersBranches`, `ClearOffersCategoriesByCommonOfferId`, `ClearOffersCompanyLogos`,
`ClearOffersCompetencesLanguages`, `ClearOffersEducations`, `ClearOffersEmploymentsFroms`,
`ClearOffersEmploymentsTypes`, `ClearOffersExperiences`, `ClearOffersHtml`, `ClearOffersMapCoordinates`,
`ClearOffersNewCategories`, `ClearOffersSalaries`, `ClearOffersSalaryRanges`, `ClearOffersToDeleteFromArchive`,
`ClearOffersTypesOfContracts`, `ClearOffersWorkSchedules`.

### Reachability — read this before rating the severity

All 21 hang off `ClearCommonOffers`, and `ClearCommonOffers` is commented out in `ClearCompanies.cs:40`
(`//new ClearCommonOffers(companyId, loggerFactory),`). I checked each of the 21 against the 48 uncommented direct
children of `ClearCompanies`: none appears there. **So no data is being lost today.**

That is also why this is worth a rule rather than just a bug report. The entire offer-clearing subtree was written
with the same misunderstanding, it sits behind a single commented-out line that plainly represents unfinished work,
and nothing in the build, the tests or the current analysers says a word about it. Uncommenting that line turns a
one-company purge into a global wipe of twenty-one tables.

### Why nothing catches it today

No `S` rule and no GP rule inspects the shape of a DML statement. `S2077` is about dynamic formatting, `S2857`
about keyword spacing. There is no test coverage over these commands either.

### Detection

A SQL string literal containing a `DELETE` or `UPDATE` statement with no `WHERE`. No schema knowledge, no
dataflow, no naming heuristics.

The one piece of real work is statement splitting: the rule has to see that `DELETE x` followed by `SELECT` is two
statements while `DELETE x FROM x JOIN …` is one. That distinction *is* the bug, so it cannot be skipped — and note
that splitting on `;` or on newlines finds none of the 21, because none of them has a separator. Split on
statement-introducing keywords (`SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `WITH`) at parenthesis depth zero,
and treat `FROM`, `OUTPUT` or `WHERE` immediately following the delete target as continuations of the same
statement.

Reporting `UPDATE` without `WHERE` costs nothing once the splitter exists.

### FP profile

Deliberate full-table deletes exist — clearing a staging table, resetting a fixture — but they are rare in `Main`
scope and `TRUNCATE TABLE` is the idiomatic form. `ClearLayouts.cs` in this same directory shows the compliant
multi-statement shape: `UPDATE … WHERE …; DELETE FROM … WHERE …`, both qualified, so a correct implementation must
not flag it.

Two neighbouring defects found while checking reachability, both also dormant and both worth a mention to the
team: `ClearNavisionBankAccounts.cs` targets `schCustomers.[schCustomers.tNavisionBankAccounts]`, a malformed
two-schema identifier that would fail at runtime (commented out at `ClearCompanies.cs:19`), and
`ClearSentRequestForPaymentHeaders` is commented out at line 46.

---
## GP0128 — an optional positional member on a record used for equality

**Confidence: high on the hazard, medium on any single call site misfiring. Value: high.**

### Evidence

`GP.Odra/src/GP.Odra.Credits/Models/Product.cs` gained a fourth positional member this year:

```csharp
public record struct Product(int OptionId, int? ParameterN = null,
                             AdditinalParameters? Parameters = null,
                             int RestrictionLevel = int.MaxValue) : IEquatable<Product>
```

Exactly one construction site passes `RestrictionLevel`
(`src/GP.Odra.Orders/Products/ProductsMapper.cs:49`, via `CalculateRestrictionLevel(...)`). Around nineteen others
take the default. A record struct's generated equality includes every positional member, and `Product` is compared
by value in the credit-consumption path:

- `src/GP.Odra.CreditsConsumption/Models/FulfilledService.cs:15` — `SuitableProducts.Contains(reservedCredit.Product)`
- `src/GP.Odra.CreditsConsumption/CreditsConsumptionService.cs:30` — `sp == cr.Product`
- `src/GP.Odra.CreditsReservation/Models/ReservationRequirement.cs:25` — `new HashSet<Product>(suitableProducts)`

The clearest sign that provenance really is mixed:
`src/GP.Odra.Orders/Credits/Consumptions/CreditsConsumptionService.cs:31` rebuilds a product from parts —
`new Product(mainService.Product.OptionId, mainService.Product.ParameterN)` — silently resetting **both**
`Parameters` and `RestrictionLevel` to their defaults, and line 37 then puts that value into `SuitableProducts`.
Comparing it against a fully populated `Product` cannot match.

`Parameters` was already optional before `RestrictionLevel` arrived, so the hazard pre-existed and was widened.
Which credit is judged suitable is a billing decision, so a silent equality change here is not cosmetic.

### Why nothing catches it today

`GP0085` reports default struct equality, but only when the struct has *no* `Equals` override — its check is
`method.ContainingType.SpecialType == SpecialType.System_ValueType`. A record struct has compiler-generated
`Equals`/`GetHashCode`, so `GP0085` skips it by design. No `S` rule covers it either.

### Detection

Decidable without history: a `record` or `record struct` that has a positional parameter with a default value, and
whose type is used in an equality context somewhere in the compilation — `==`/`!=`, `Contains`, `Distinct`,
`Except`, `Union`, `HashSet<T>`, or as a `Dictionary<T,_>` key.

The argument is not that defaults are wrong, it is that a defaulted member silently participates in identity while
callers do not think of it as part of identity. The fix is either to exclude it from equality (custom `Equals`) or
to make it non-optional so every construction site has to state it.

Report on the record declaration, and mention the member. Do not report records never used in an equality context —
that is what keeps this from firing on every DTO.

---

## GP0129 — an identifier column typed as NVARCHAR(MAX) in a migration

**Confidence: high. Value: medium.**

### Evidence

`GP.Odra/src/GP.Odra.Database/Migrations/M20260119_1800_AllocationsEditSessionsTable.cs` creates
`schOrders.tAllocationsEditSessionsSwaps` with four identifier columns as `.AsString(int.MaxValue)` —
`CreatedAllocationId`, `TopUpId`, `WalletId`, `RollBackTopUpId`. NVARCHAR(MAX) cannot be indexed, cannot take part
in a key, and forces off-row storage. The same table's own primary key uses `SwapId` as `.AsString(112)`.

The inconsistency is visible within the same repository: `M20260529_1000_AddTopUpResultToComplimentaryAllocations.cs`
declares `WalletId` as `.AsString(256)` **and indexes it**. So one logical column is bounded and indexed in one
table and unbounded in another.

### Detection

Syntactic. Flag `.WithColumn(name).AsString(int.MaxValue)` (and `.AsString(-1)`) in a FluentMigrator migration
where `name` ends in `Id`. A stronger second check, still decidable, is to flag any `AsString(int.MaxValue)` column
that a `Create.Index`/`PrimaryKey` in the same compilation references — indexing a MAX column fails at runtime.

Note the neighbouring rule: `GP0119` already polices raw DDL in migrations, so migration-shaped rules have a home.

---

## FINDING — a command named `Anonymize…` does not anonymize

Not a rule candidate — an analyzer cannot know which columns are personal data. Raise with the owning team.

`GP.Odra/src/GP.Odra.Internal/Companies/Purging/DataAccess/AnonymizeUsersAssignedToCompanyExclusively.cs`, for
users belonging exclusively to the company being purged, runs:

```sql
UPDATE schCustomers.tUsers
   SET isActive = '0', admUnitID = NULL, userLogin = userLogin + '*' + @dateString
 WHERE userID IN (SELECT userID FROM @usersTable)
```

It deactivates the account, nulls `admUnitID` and suffixes the login. `userFirstName`, `userLastName` and
`positionName` are left as they are, and the login itself is preserved rather than replaced. I checked the rest of
the purge chain: the only writers of those three columns anywhere in the repository are ordinary update commands
(`Customers/Users/Sql/UpdateUserCommand.cs`, `IdmApi/Data/Commands/UpdateUser.cs`), neither of which is in
`ClearCompanies`' command list. So after the "anonymisation" the identifying fields are still there.

Four smaller defects in the same statement:

- `DATEPART(dd/mm/hh/mi/ss)` is cast to `NVARCHAR(2)` with no zero-padding, so the suffix is variable width and
  ambiguous — day 1 with month 12 and day 11 with month 2 both render as `112`.
- `userLogin + '*' + @dateString` has no length guard. If the column is near capacity SQL Server raises *String or
  binary data would be truncated* and the whole purge transaction rolls back.
- `GETDATE()` rather than `GETUTCDATE()`, in a codebase that is otherwise UTC throughout.
- `u.isPracuj = '0'` compares a numeric column against a string literal.

---
## Investigate — an idempotency check separated from its write by a network call

**One confirmed risk. No detection proposed.**

`GP.Wierzbiak/src/GP.Wierzbiak.Api/Services/ComplimentarySwapServiceWriter.cs`, `AdhibitAsync`:

```csharp
var existingAdhibition = await _swapRepository.GetAdhibitionBySwapIdAsync(swapId, ct);
if (existingAdhibition is not null) return Success(existingAdhibition.ComplimentaryAllocationId);
...
foreach (var product in swap.Products) { await _eligibilityClient.CheckAsync(...); }   // network
...
var createdId = await _swapRepository.AdhibitAsync(allocation, allocationProducts, adhibition, ...);
```

Two concurrent calls both find no adhibition, both pass eligibility, both mint a fresh `Ulid` allocation id and
both write — granting the complimentary products twice, unless a unique constraint on `ComplimentarySwapId` stops
the second. The window is as wide as the eligibility calls, one per product.

Worth checking whether that constraint exists; the code does not rely on it (there is no duplicate-key handling,
which `GP0111` would be the rule for). I have no low-false-positive detection for "check-then-act with a network
call in the middle" — `GP0008` only covers external calls *inside* a transaction, which this is not. Recorded so
the finding is not lost.

---
## Investigate — `DateTime.Now` where the codebase means UTC

**Confidence: low as a rule. Noise risk high.**

26 occurrences. Distribution matters more than the count:

- 19 in `extranet2` (legacy WebForms/.NET Framework: `OrdersSrv.cs`, `CreditsModificationFcd.cs`, `Global.asax.cs`)
- 7 in `juno/src/Juno/GP.Juno.Abstractions/SystemTime.cs` — the time abstraction itself, legitimately defines `Now`
- 2 in modern services: `GP.Tomorrowland/.../AccessControlService.cs:71`, `GP.MalaMi/.../ClassificationProcessor.cs:92`

So a rule would fire almost entirely on legacy code that nobody is going to change, for 2 real hits. `GP0017` and
`GP0018` already police the *naming*/`DateTimeKind` side. Only worth it if scoped to assemblies that use the
`*Utc` convention, and probably not worth it at all.

---

## Not a new rule — GP0027 had a stale comment, but handles the current API

`HttpCallShouldPropagateCancellationToken.cs` carries this comment:

> Verified against the submodules/juno source: none of the GP.Juno fluent API surface (IHttpClient.Send,
> IHttpClientBuilder.Service, nor any HttpRequestProperties extension such as GetJson/PostJson/…) exposes an
> overload accepting a CancellationToken anywhere, so those calls can never propagate one and must not be reported.

That is not true of the entire surface in use today. `submodules/juno/src/Juno/GP.Juno.Abstractions/HttpApiClient/
HttpSending/HttpMethods/HttpSenderHttpMethodsExtensions.cs` declares `Get`, `Post`, `Put`, `Delete` on
`HttpSender` with a **mandatory** `CancellationToken cancellation`, and
`Json/HttpSenderResponseJsonExtensions.cs:16` does the same for `ReceiveJson<T>`.

Verification showed that the implementation is already correct for the request methods: `GpHttpCallHelper`
recognizes reduced extension methods on `HttpSender`, and `CancellationTokenParameter` inspects
`method.ReducedFrom.OriginalDefinition`, where it finds the mandatory token. A regression for
`HttpSender.Get(CancellationToken.None)` was added to `HttpCallShouldPropagateCancellationTokenTest`; it is
reported when a caller token is available. The stale comment in GP0027 was corrected to distinguish the legacy
`GP.Juno.HttpClient` builder, which has no token overload, from the newer `HttpSender` API.

`ReceiveJson<T>` is not classified as an outgoing HTTP request by GP0027. That is intentional for now: it
deserializes an already received response, and a correctly diagnosed request call in the same chain is the
higher-value cancellation boundary.

---

## Code findings that need fixing regardless of any rule

All of these now have issues in their owning repositories, indexed with links in
[gp-product-findings.md](gp-product-findings.md) — including the ones I checked and deliberately did not file, so
nobody re-investigates them.

1. **`GP.Plunger` — SOQL injection**, 3 sites, `IPracujClubMembershipService.cs:145,242,279`. Values arrive from
   MassTransit message fields. Fix by parameterising or, at minimum, escaping `'` before interpolation.
2. **`GP.San` — migration targeted the wrong table.** Commit `8a4f9c532`, `Migration0084.cs`: `AllocationType`
   was added to `DbSchema.DbTables.FundsDemandPlans` instead of `AllocationsDemandRules`. Already fixed. Worth
   noting that `Down()` was wrong in the same way, so a "Down must invert Up" rule would not have caught it, and
   both table constants are valid so it compiled and ran. I found no low-FP detection for this — recorded here so
   the idea is not lost.
3. **`SELECT *` in production queries** — 38 occurrences, 22 of them in `GP.Odra`. Low value as a rule
   (`S`-rules do not cover it and it is often deliberate), but it makes GP0117-style column checking impossible on
   those queries.

---

## Measurements from the sweep

Counts of added production lines for the whole year, kept because they are the raw material for judging any future
candidate in this area — not as an argument that any particular rule should exist.

| pattern | occurrences |
| --- | --- |
| `CancellationToken.None` | **205** |
| raw `SELECT … FROM` string literals | 226 |
| `[Authorize]` | 95 |
| `ProducesResponseType(401\|403)` | 125 |
| `WITH(NOLOCK)` | 59 |
| `SELECT *` | 38 |
| interpolated query text | 130 (≈127 of them schema/table identifiers — see GP0120) |
| `.ToDictionary(` | 124 |
| `OFFSET`/`FETCH NEXT` paging | 12 |
| sync-over-async (`.Result;`, `GetAwaiter().GetResult()`, `.Wait()`) | 22 |
| `DateTime.Now` / `DateTimeOffset.Now` | 26 |
| `async void` | 0 |
| empty `catch {}` | 1 |
| `lock(this)` / `lock(typeof(…))` | 0 |

The last three lines matter as much as the first: the classic smell inventory is essentially clean, which is why
the candidates worth chasing are all in the domain/contract/SQL layer rather than in general C# hygiene.

The `CancellationToken.None` count is the one to weigh carefully. It is large, and the removed GP0116 was built on
it, but volume was never the objection — precision was. 205 occurrences say the pattern is pervasive, not that a
syntactic rule can tell the legitimate opt-outs from the broken chains.

---

## Implementation notes — traps hit while building GP0115–GP0118

Still accurate even though three of those four rules were removed: these are properties of the build
environment, not of the rules. `GpSqlText.cs` is gone from the tree but recoverable from `f1c6cbd89`.

The analyzer project targets `netstandard2.0` with an old `System.Collections.Immutable`. Every one of these cost
a build cycle:

- **No collection expressions on `ImmutableArray<T>`** — `return [];` fails with `CS9210`. Use
  `ImmutableArray<string>.Empty`. Collection expressions on plain arrays (`char[] x = ['a']`) are fine.
- **No `System.Index`** — `text.Split('.')[^1]` fails with `CS0518`/`CS0656`. Index manually.
- **`SyntaxKind.DefaultLiteralExpression` does not exist**; use `SyntaxKindEx.DefaultLiteralExpression` via
  `RawKind`, the way `Rules/CancellationTokenShouldBeUsed.cs:245` does.
- **Raw string literals (`"""…"""`) are `LiteralExpressionSyntax` of kind `StringLiteralExpression`** — one
  registration covers quoted, verbatim and raw forms. Only the *token* kind differs.
- **A multi-line literal is reported on the line it starts on.** Test expectations must sit above the declaration
  as `// Noncompliant@+1 {{…}}`; a comment trailing the closing quote asserts the wrong line.
- **`.cs` files must be UTF-8 *with BOM* and LF.** Writing them with a plain editor/heredoc silently omits the
  BOM.
- **Tests needing `Microsoft.AspNetCore.Authorization`**: `AspNetCoreMetadataReference` does not expose it and is
  an upstream file. Use `Rules/GP/GpMetadataReferences.cs`, which derives the shared-framework folder from a
  reference that class already resolves.
- **Run `ConcurrentExecutionTest`** in addition to the rule's own tests whenever a rule keeps state across
  actions (`RegisterCompilationStartAction` + `RegisterCompilationEndAction`), and `RuleCatalogTest` to confirm
  the rspec files are picked up.

Verification commands are in `AGENTS.md`. Note that `mvn clean install` was **not** run for GP0115–GP0118.
