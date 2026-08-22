# GP rule backlog

Candidates found by reviewing every commit on the default branch of all 132 active repositories in
`C:\Git\runn for all\repos` between **2026-01-01 and 2026-08-22** (14 378 commits, 445 387 added C# lines,
223 549 of them outside test paths).

Read `AGENTS.md` first — it owns the conventions.

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

## Investigate — `ToDictionary` over pairs that legitimately repeat

**Confidence: low. Do not implement without a sharper formulation.**

### Evidence

`GP.Scylla`, commit `d8bc4f8f5` — *"fix: RecommendedJobOffersQueryExtensions created wrong type of array query
params"*:

```csharp
-        var pairs = model.AsQueryParams().ToDictionary(x => x.Key, x => x.Value);
+        var pairs = model.AsQueryParams();
```

`AsQueryParams` was an iterator yielding `KeyValuePair<string,string>`, including several entries **sharing a key**
for array parameters (`EnumerateCollectionQueryParams`). The fix also changed the element type to Juno's
`StringPair`. 124 `.ToDictionary(` calls were added across the year, so the pattern is common.

### Why it is hard

"This sequence can contain duplicate keys" is not statically decidable in general. A narrow version — flag
`.ToDictionary(x => x.Key, …)` applied to an `IEnumerable<KeyValuePair<,>>` produced by an iterator that yields
inside a loop — is decidable but fragile and easy to route around. A different framing that might be worth more:
**query parameters should be passed as Juno's `StringPair` sequence, never as a dictionary**, which is a Juno-API
convention rule rather than a correctness rule, and would have caught this exact commit.

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

## Not a new rule — GP0027 has a stale assumption that silences it

**Confidence: high. This is a bug in a shipped rule, not a candidate.**

`HttpCallShouldPropagateCancellationToken.cs` carries this comment:

> Verified against the submodules/juno source: none of the GP.Juno fluent API surface (IHttpClient.Send,
> IHttpClientBuilder.Service, nor any HttpRequestProperties extension such as GetJson/PostJson/…) exposes an
> overload accepting a CancellationToken anywhere, so those calls can never propagate one and must not be reported.

That is not true of the surface in use today. `submodules/juno/src/Juno/GP.Juno.Abstractions/HttpApiClient/
HttpSending/HttpMethods/HttpSenderHttpMethodsExtensions.cs` declares `Get`, `Post`, `Put`, `Delete` on
`HttpSender` with a **mandatory** `CancellationToken cancellation`, and
`Json/HttpSenderResponseJsonExtensions.cs:16` does the same for `ReceiveJson<T>`. Whoever picks this up should
re-derive which Juno surface `GpHttpCallHelper` actually matches and check that `CancellationTokenParameter` is
not rejecting calls it should report. Note that `GP0116` only covers the disjoint case (no token anywhere in the
enclosing scope), so a gap here is not covered by it.

---

## Code findings that need fixing regardless of any rule

These are defects in product code, not rule candidates. They should go to the owning teams.

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
