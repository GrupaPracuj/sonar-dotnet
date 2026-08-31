# Working in this repository

This is **GrupaPracuj's fork of [SonarSource/sonar-dotnet](https://github.com/SonarSource/sonar-dotnet)**. It exists for
one reason: to ship our own C# analyzer rules — the **GP rules** — as a companion Roslyn analyzer plugin that runs
*alongside* the official C# plugin rather than replacing it.

Everything in this file follows from one rule.

## The rule

> **We build GP rules and the tests for them. Everywhere else, upstream wins, and changes are kept to the absolute
> necessary minimum.**

"Upstream wins" is not a preference, it is how the fork stays mergeable. Every line we change in a file that upstream
owns is a line somebody has to resolve by hand at every future sync, forever.

## What we own

| Path | Contents |
| --- | --- |
| `analyzers/src/SonarAnalyzer.CSharp/Rules/GP/` | rule analyzers, their code fixes, and shared `Gp*` helpers |
| `analyzers/rspec/cs/GP/` | one `GPxxxx.json` + one `GPxxxx.html` per rule |
| `analyzers/tests/SonarAnalyzer.Test/Rules/GP/` | one test class per analyzer |
| `analyzers/tests/SonarAnalyzer.Test/TestCases/GP/` | test-case corpora for those tests |
| `sonar-csharp-plugin/src/main/java/org/sonar/plugins/csharp/Gp*.java` | rules definition, `GpRoslynRules`, and the **"GP way"** built-in quality profile |
| `scripts/validate-rule-metadata.ps1` | rspec metadata gate, run by `build-gp-csharp-plugin.bat` before anything is built |

Add new work here. A new rule needs an analyzer, an rspec `.json` + `.html`, and a test — nothing outside these paths.

## Upstream files we deliberately change

This list is short on purpose, and every entry exists because GP would not work without it. **Do not extend it without
an explicit decision** — if a change seems to need a new entry, first look for a way to do it inside our own paths.

| File | Why |
| --- | --- |
| `sonar-csharp-plugin/src/main/java/.../CSharpPlugin.java` | makes this plugin `GP-csharp` with repository key `roslyn.GPcsharp.cs` and registers the GP extensions instead of `CSharpCoreExtensions` |
| `sonar-csharp-plugin/src/main/java/.../package-info.java` | header, kept consistent with the above |
| `sonar-csharp-plugin/src/test/java/.../CSharpPluginTest.java`, `CSharpRulesDefinitionTest.java` | cover our own wiring |
| `sonar-dotnet-core/src/main/java/.../DotNetRulesDefinition.java` | makes `repositoryName()` overridable so the GP repository can be named |
| `analyzers/src/RuleCatalog.targets` | `**/*.json` / `**/*.html` globs, so `rspec/cs/GP` reaches the rule catalog |
| `pom.xml`, `sonar-*/pom.xml` | the `${revision}` version mechanism, GP artifact names/keys, the GP analyzer zip, the plugin size gate, the license-header check turned off in `sonar-csharp-plugin` |
| `scripts/set-version.ps1` | adapted to the `${revision}` mechanism |
| `.gitmodules`, `.github/workflows/gp-csharp-linux-validation.yml`, `build-gp-csharp-plugin.bat` | our submodules and our CI/build entry points |

## Never revert upstream

When upstream changes an API we depend on, **GP adapts to upstream** — we do not restore the old shape.

This is not hypothetical. The fork's first commit (`f10abdd3e`) rolled back an upstream refactor across ~160 files
(file-scoped namespaces) and deleted upstream additions along the way, including the whole of rule S9118. None of it
was intentional and all of it had to be undone later, by hand, before the fork could be synced.

A concrete example of adapting the right way: upstream turned helpers such as `IsControllerActionMethod`,
`IsControllerType`, `IsCoreApiController` and `IsRecord` from extension *methods* into extension *members*, and made
`ToExecutionOrder()` yield `IOperation` directly instead of a wrapper. The fix was to drop the parentheses and the
`.Select(x => x.Instance)` in our GP files — not to keep the old helpers alive.

## Syncing with upstream

1. `git fetch upstream && git merge upstream/master`
2. For every conflict **outside** the two lists above, take upstream's version wholesale
   (`git checkout upstream/master -- <path>`). Do not hand-merge it.
3. Watch for files only *we* touched: those merge cleanly and silently keep our version, which is exactly how a stale
   revert survives. Compare `git diff <merge-base> HEAD` against the list above and take upstream for anything not on it.
4. Resolve the listed files in our favour, re-applying our change on top of upstream's new content rather than keeping
   our old file.
5. Build, then run the tests, then fix GP to match upstream's new APIs.

## Rule conventions

- **Precision over quantity.** Fewer rules, each with as close to zero false positives as we can get. A rule that
  floods an existing codebase with true-but-low-value findings is not worth shipping.
- Before adding a rule, check whether an `S`-rule already covers it — and read that rule's *implementation*, not just
  its title. Titles undersell scope, and duplicates have slipped through on title alone.
- Rule ids are sequential and never reused. Highest so far is **GP0132**, so the next free id is **GP0133**; gaps are
  removed rules and stay empty.
- Decide by Roslyn semantics, not by identifier spelling, whenever the two can disagree.
- Every rule needs an rspec `.json` (`sqKey`, `scope` must be `Main` or `Tests` — never `All`) and an `.html` with the
  standard *Why is this an issue / How to fix it / Noncompliant* sections.
- Rspec metadata is validated against the plugin API enums by `scripts/validate-rule-metadata.ps1`, which the build runs
  first. Note that `code.impacts` only accepts `MAINTAINABILITY`, `RELIABILITY` and `SECURITY` — there is no
  `PERFORMANCE` quality, and an invalid value stops SonarQube during startup rather than just disabling the rule.
- Known and accepted false negatives are documented in place with a `// FN: reason` comment.
- All `.cs` files are UTF-8 **with BOM** and LF line endings (`.editorconfig`, `.gitattributes`). GP files carry the
  Grupa Pracuj licence header; upstream files keep theirs untouched.

## Verifying a change

```sh
dotnet build analyzers/SonarAnalyzer.sln
dotnet test analyzers/tests/SonarAnalyzer.Test/SonarAnalyzer.Test.csproj -f net10.0 \
  --filter "FullyQualifiedName~SonarAnalyzer.Test.Rules.GP"
dotnet test analyzers/tests/SonarAnalyzer.Test/SonarAnalyzer.Test.csproj -f net48 \
  --filter "FullyQualifiedName~SonarAnalyzer.Test.Rules.GP"
```

Run both target frameworks: a reference or language-version problem often shows up on only one of them.

For the plugin itself, see `docs/contributing-plugin.md`: `dotnet build analyzers/SonarAnalyzer.sln` followed by
`mvn clean install` from the repository root.

A handful of non-GP tests fail on a Polish-localized Windows machine because they assert on English CLR wording. Do not
chase those; compare against a clean worktree at `HEAD` before assuming a change caused a failure.

When a test corpus lives in `TestCases/`, remember it is compiled a second time wrapped in
`namespace AppendedNamespaceForConcurrencyTest`. A corpus that *declares* stub types inside a framework namespace
(`namespace MassTransit`, `namespace Consul`, ...) breaks in that pass; either reference the real package or set
`.WithConcurrentAnalysis(false)` with a comment explaining why.
