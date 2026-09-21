# The Flow — jak budujemy aplikacje z agentem

Konkretny opis tego, co realnie dzieje się, gdy ktoś zakłada nową usługę `pd.toolem`
i pisze ją z agentem AI. Nie jest to strona marketingowa — to mapa mechanizmów: gdzie
agent dostaje kontekst, skąd bierze wspierane API i kto niezależnie sprawdza wynik.
Każdy punkt niżej wskazuje realny plik, atrybut albo narzędzie, nie ogólną obietnicę.

## Problem, który to rozwiązuje

Model zna C#. Nie zna naszej architektury, wersji bibliotek Juno, właścicieli usług ani
istniejących kontraktów. Bez tego generuje kod poprawny, ale nie nasz — wybiera popularną
bibliotekę zamiast zatwierdzonej ścieżki, buduje integrację od zera zamiast użyć usługi,
która już jest, a naruszenie standardu wychodzi dopiero w PR albo CI, kiedy nikt nie pamięta
już kontekstu zmiany.

The Flow atakuje to z dwóch stron: **wstrzykuje kontekst w momentach, w których agent
podejmuje decyzję**, i **niezależnie weryfikuje wynik, zanim trafi do review**.

Ważne rozróżnienie: skille i `AGENTS.md` to warstwa **miękka** — pomagają agentowi, ale
model może je zignorować. Nie zastępują niezależnej kontroli. Struktura projektu, ostrzeżenia
`[Obsolete]` w kompilatorze oraz analiza SonarQube (103 reguły GP) w Connected Mode i CI
automatycznie oceniają wynik. Skille naprowadzają; kompilator i analiza dostarczają feedbacku,
a decyzja o zmianie nadal należy do developera.

## Krok 1 — `pd.tool`: scaffold projektu .NET z kontekstem dla agenta

`pd.tool` to scaffolder solucji .NET (`dotnet new`). Nie zakłada repozytorium — generuje
strukturę projektu (hosty, `Contracts`, `Core`, testy, workflowy, konfigurację SonarQube), a
razem z nią, przez template `pd-tool-agent-instructions`
(`identity: GrupaPracuj.Dev.Tool.Templates.AgentInstructions`), warstwę kontekstu dla agenta:

- **`AGENTS.md`** z zasadami architektury oraz wskazówkami dla kontraktów, obsługi błędów,
  konfiguracji i testów.
- **`.github/copilot-instructions.md`** — przekierowanie do `AGENTS.md`, żeby był jeden
  kanon, a nie dwa rozjeżdżające się źródła.
- **Trzy skille** w `skills/`: `contracts-and-errors`, `reference-application`, `testing`.
- **Konfigurację SonarQube Connected Mode** — plik `.sonarlint/connectedMode.json` z
  `sonarQubeUri: https://sonar2.pracuj.pl` i `projectKey: GP.ApplicationName`. Spina scaffoldowany
  projekt z serwerowym profilem 103 reguł GP od razu po wygenerowaniu.
- Sekcję **„Organizational capabilities"**: przed użyciem zewnętrznej usługi lub komponentu
  platformowego agent ma sprawdzić katalog Plateau przez MCP.

## Krok 2 — sprawdzone aplikacje jako punkt odniesienia

Skill `reference-application` prowadzi do dwóch realnych aplikacji, z których każda ma własne
`AGENTS.md` i `skills/`:

- **`GP.Reference.BookLibrary`** (consumer) — katalog i wypożyczenia, persystencja SQL
  z migracjami, publikacja do Event Stream, wychodzące HTTP do providera.
- **`GP.Reference.Bibliography`** (provider) — REST provider, uwierzytelnianie bearer
  z autoryzacją claimową, cienki host bez warstwy infrastruktury.

Referencje są przykładem do adaptacji, nie kodem do ślepego kopiowania.

## Krok 3 — Juno: wspierane API przychodzą razem z pakietem

Juno to nasze biblioteki infrastrukturalne (wychodzące HTTP, ADO/SQL, Event Stream, hosting).
Trzy mechanizmy kierują agenta na aktualne API zamiast na wzorzec z internetu:

- **Skille materializowane z pakietu.** `GrupaPracuj.Juno.targets` (dostarczany w
  `buildTransitive/`) po `Build` kopiuje skille do `<repo-root>/skills/juno/`. Zestaw jest
  wersjonowany z pakietem i nie trafia do commitów konsumenta. Pięć skilli:
  `ado-connection`, `aspnetcore-hosting`, `event-stream`, `http-api-client`, `package-map`.
- **Stare ścieżki jako `[Obsolete]` z migracją.** Ostrzeżenie pojawia się przy buildzie,
  niezależnie od używanego agenta, a atrybut niesie konkretną instrukcję migracji, np.:
  `SystemTime` → `TimeProvider`, Juno `HttpClient` → `IHttpSenderFactory` / `HttpSender`,
  `System.Data.SqlClient` → `UseMicrosoftMsSql`.
- **Dokumentacja XML.** Publiczne API Juno ma opis dostępny w IntelliSense, więc agent czyta
  kontrakt zamiast go zgadywać.

## Krok 4 — Plateau: wiedza organizacji przez MCP

Plateau to katalog Backstage naszych usług. Profil MCP `plateau` udostępnia read-only
narzędzia: `get-catalog-model-description`, `query-catalog-entities`, `search.query`. Dzięki
temu agent, zanim zbuduje własną integrację, może sprawdzić: co już istnieje, **za co dana
usługa odpowiada**, kto jest właścicielem (`spec.owner` / `relations.ownedBy`), jaki cykl życia
ma komponent i jakie API oraz OpenAPI wystawia.

**Konfiguracja (Backstage MCP w Claude):**

```sh
claude mcp add --transport http plateau https://plateau-api.pracuj.pl/api/mcp-actions/v1/ --header "Authorization: Bearer $(printenv PLATEAU_MCP_TOKEN)"
```

Token `PLATEAU_MCP_TOKEN` trzymamy w Keeperze — nie w repo ani w historii shella.

## Krok 5 — SonarQube: niezależny checker, nie druga opinia modelu

103 reguły GP (analizatory Roslyn, repozytorium `roslyn.GPcsharp.cs`) to **reguły in-house
oparte na standardach naszej organizacji** — działają obok wbudowanego zestawu SonarQube i
**rozbudowują jego katalog reguł**, a nie go zastępują. Połączenie jest przygotowane już przez
pd.tool: wygenerowany `.sonarlint/connectedMode.json` wiąże projekt z `https://sonar2.pracuj.pl`.

- `GP0020` — endpoint musi jawnie deklarować politykę dostępu,
- `GP0023` — sekrety nie mogą lecieć w URL,
- `GP0135` — dane osobowe nie w URL,
- `GP0136` — nie loguj całych obiektów HTTP / auth,
- reguły guardraili AI (m.in. `GP0141`, `GP0143`, `GP0145`, `GP0146`).

Przez SonarQube MCP na maszynie użytkownika finding wraca do lokalnej pętli agenta razem z
opisem reguły. Generator (agent) i checker (Sonar) to różne role: reguły używają semantyki
Roslyn, nie dopasowania nazw. Connected Mode i CI sprawdzają kod niezależnie od tego, czy
agent przeczytał instrukcje lub skille.

**Konfiguracja (SonarQube MCP — Copilot i Claude):**

- konfigurator: <https://mcp.sonarqube.com/config-generator.html>
- README: <https://github.com/SonarSource/sonarqube-mcp-server>

Dla naszego serwera ustaw `SONARQUBE_URL=https://sonar2.pracuj.pl` i user-token
`SONARQUBE_TOKEN` z Keepera.

## Krok 6 — człowiek i CI zostają bramką

Developer ocenia rozwiązanie, zasadność ewentualnych wyjątków i gotowość do review. Testy oraz
quality gate w CI to niezależna weryfikacja poza lokalną pętlą agenta.

## Skąd co pochodzi — trzy warstwy kontekstu

| Warstwa | Źródło | Co dostarcza |
| --- | --- | --- |
| Projekt (scaffold) | `pd.tool` | `AGENTS.md`, `copilot-instructions`, skille, aplikacje referencyjne |
| Platforma | Juno | skille, XML docs, `[Obsolete]` z migracją |
| Organizacja | Plateau + SonarQube | katalog, odpowiedzialności, ownership, OpenAPI, 103 reguły GP |

## Status

**Działa dziś:** scaffold i skille pd.tool, Connected Mode, aplikacje referencyjne, Juno,
103 reguły GP oraz katalog Plateau.

**Po konfiguracji:** SonarQube MCP oraz Plateau MCP na maszynie użytkownika.

**Rozwijamy:** TechDocs Plateau w indeksie MCP, kolejne źródła wiedzy platformowej i następne
wysokoprecyzyjne reguły GP.
