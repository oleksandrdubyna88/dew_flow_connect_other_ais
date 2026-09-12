# PLAN — review roles become data (1 of 5: the catalog, with nothing visible changing)

> Status: **IMPLEMENTED, 2026-09-12.** Scope: `src_mcp/core/Rounds`
> (`PromptCatalog` → `RoleCatalog`, `PanelConfig`), `src_mcp/runners/Reviewers` (the `ReviewRole`
> enum and the six `Build` implementations), `src_mcp/src/Server` (`PanelSettings`, `RolePrompts`,
> `PanelService`, `Tools`), `src_server/src/Jobs` (the enum's three readers), a new
> `shared/builtin-roles.json`, `src_vs_code/src/prompts.ts` with a generated sibling, two
> generator scripts, and the tests on both sides.
>
> Related docs: [module_core.md](module_core.md),
> [module_server.md](module_server.md),
> [module_extension.md](module_extension.md),
> [module_team_server.md](module_team_server.md),
> [architecture.md](architecture.md);
> [PLAN_conventions_is_its_own_role.md](PLAN_conventions_is_its_own_role.md) — the last
> time a role was added, and the change surface it cost;
> [PLAN_per_role_gate_and_dealt_prompts.md](PLAN_per_role_gate_and_dealt_prompts.md) —
> why a role already carries its own rounds and threshold.

## What the operator asked for

> For developers, in every language, this is superb. But a manager I spoke to today needs the same
> thing for checking and producing documents — CVs, product descriptions, whatever the work is. We
> cannot think of every case in advance. So: a button to add review roles, a tab with a CRUD list, and
> per role — a name that shows in *Prompts per round*, a mandatory general prompt, more named prompts
> on a button, an active switch (active ones appear in *Prompts per round* and can be ticked there as
> today; inactive ones do not), a plan/result switch, and a programming-task yes/no switch so the MCP
> knows whether the result is a diff or a document. Rework the current prompts into this structure.
> Built-in roles cannot be renamed or deleted; their prompt text can be edited and prompts added;
> shipped prompts cannot be deleted. Five plans, one pull request each.

*(translated from the operator's Russian; this repository's documentation is English.)*

This is the **first** of the five. It makes the roles and their prompts DATA — loaded from one seed
on both halves, extendable through the settings channel — while changing **nothing anyone can see**.
The other four build on it and are named at the end; none of them can start before this one lands,
and this one must not be built in parallel with anything that touches the files it names.

## The symptom, before any solution

A role is a compile-time constant in **five places**, and a manager's "check this CV against these
rules" cannot be expressed without recompiling both halves:

| Where | What it is |
|---|---|
| [ReviewerRuntime.cs:9-22](../src_mcp/runners/Reviewers/ReviewerRuntime.cs#L9-L22) | `enum ReviewRole { PlanCritique, Conventions, Architecture, SecurityReliability, UxDxPerformance }` — a CLR enum; it cannot hold a name a person typed |
| [PromptCatalog.cs:36-93](../src_mcp/core/Rounds/PromptCatalog.cs#L36-L93) | five `const string` role names and a static `ImmutableArray` of 25 `PromptChoice` rows |
| [SessionState.cs:87-93](../src_mcp/core/Rounds/SessionState.cs#L87-L93) | `PanelConfig.AllRoles` / `CodeRoleNames`, two `static readonly string[]`; [`RolesOf(stage)`](../src_mcp/core/Rounds/SessionState.cs#L150-L151) picks between them |
| [prompts.ts:22-66](../src_vs_code/src/prompts.ts#L22-L66) | the panel's hand-typed mirror of both lists — `ROLES` and `PROMPTS` |
| [ReviewEndpoints.cs:209-213](../src_server/src/Jobs/ReviewEndpoints.cs#L209-L213), [JobKind.cs:124-126](../src_server/src/Jobs/JobKind.cs#L124-L126), [ReviewLauncher.cs:209-210](../src_server/src/Jobs/ReviewLauncher.cs#L209-L210) | the Team server validates a role by `Enum.TryParse<ReviewRole>`, lists `Enum.GetNames<ReviewRole>()` in its refusal, and `RoleOf` parses the name back into the enum — **falling back to `default`, which is `PlanCritique`, for anything it does not know** |

And the thing that holds the two mirrors level is a test that reads C# source as text:
[panelServerPromptAgreement.test.ts:59-82](../src_vs_code/src/test/panelServerPromptAgreement.test.ts#L59-L82)
regexes `const string X = "…"` and `new(…, true)` out of `PromptCatalog.cs`. It has "lost two branches
to that rule" ([prompts.ts:112-116](../src_vs_code/src/prompts.ts#L112-L116)) and would lose the
whole file the moment the catalog stops being a C# array literal.

**What is NOT the symptom, and is the reason this plan is small.** Everything downstream of those
five places already treats a role as a string. `PanelConfig.Roles` is an
`IReadOnlyDictionary<string, RoleGate>` ([SessionState.cs:65-104](../src_mcp/core/Rounds/SessionState.cs#L65-L104))
with `For(role)`, `EnabledRolesOf(stage)` and `RolesForRound(stage, round)` all keyed by name; the gate
groups findings by the string `Finding.Role` ([GateRule.cs:74](../src_mcp/core/Gate/GateRule.cs#L74));
the rounds database stores it as text; and every consumer of `ReviewerInvocation.Role` calls
`.ToString()` on it first — `LiveRound.cs:34-37`, `UsageLedger.cs:112`, `RoundAudit.cs:64`,
`ReviewerExecutor.cs:276`. The vendor adapters use the role for **one thing**: the answer file's name,
`$"{Provider}-{role}.json"` ([ReviewerRuntime.cs:223](../src_mcp/runners/Reviewers/ReviewerRuntime.cs#L223)).
The enum buys a closed list of five names at compile time, and a closed list is exactly what the
operator asked to open.

The prompt-text side is even readier: `RolePrompts` already layers an override file over the embedded
default, per prompt id, and its `Override`/`RestoreDefault` methods have **no production caller** — the
class says so itself: *"The server only READS here — editing arrives with the extension"*
([RolePrompts.cs:18](../src_mcp/src/Server/RolePrompts.cs#L18)). This plan does not give them one
either (that is plan 2); it re-keys them by prompt id so that plan 2 can.

## What must be true when this is done

1. The five roles and their 25 prompts are loaded from **one file**, `shared/builtin-roles.json`, on
   both halves — embedded into `CoaiMcp.Core`, generated into the extension — and each half's loader
   is tested against that file, not against the other half's source code.
2. `ReviewRole` the enum no longer exists. A role is a `string` id from the adapter's `Build` to the
   Team server's endpoint, and `ReviewLauncher.RoleOf` is deleted rather than fixed: with no enum to
   parse into there is no `default` to fall into.
3. `coai-mcp` reads one new settings key, `COAI_ROLES` — a JSON array of role definitions — and
   composes it with the built-ins by rules that are pure, named and tested (§ The design, 4).
   Malformed JSON is no custom roles at all, never a half-applied list, the way `COAI_VENDORS` and
   `COAI_PROMPTS_PER_ROUND` already behave.
4. A custom role named in `COAI_ROLES`, with its prompt text on disk, is dealt work in a real round
   exactly as a shipped role is — on every CLI vendor and on a local engine. A Team-server vendor is
   **named as excluded** for that role rather than asked, because the server it fronts accepts only
   the five shipped names until plan 3. A custom role whose prompt text is missing is **named** in
   the round's `NotAsked` list — the `SkippedRole(Role, Reason)` the calling AI already reads — and
   the round still runs — never a crash, never a silent absence.
5. **Nothing observable changes** for a configuration that carries no `COAI_ROLES`: same five roles,
   same order, same ids, same prompts, same env keys, same refusal sentences on the Team server, same
   panel. The proof is the existing test suites on both sides passing with only mechanical renames.

## Constraints

- **Built-in ids do not change.** `PlanCritique`, `Conventions`, `Architecture`, `SecurityReliability`,
  `UxDxPerformance` stay the exact strings they are: `COAI_ROUNDS_ARCHITECTURE`, `coai.rounds`,
  `coai.promptsPerRound` and every row in every `coai.db` are keyed by them.
- **Prompt text never enters `COAI_ROLES`.** `envBlock` is also the block a person pastes into an MCP
  client's config ([serverSettingsFile.ts:8-16](../src_vs_code/src/serverSettingsFile.ts#L8-L16)).
  Text stays in `<dataDir>/prompts/<id>.md`, the layer `RolePrompts` already reads.
- **The core stays pure.** [ArchitectureTests.cs](../src_mcp/tests/ArchitectureTests.cs) holds the
  line on process and network; the filesystem is held out by review. The seed loader reads a manifest
  resource from its own assembly, which is neither.
- **Native AOT, reflection off.** Every new JSON shape is a `[JsonSerializable]` on a source-generated
  context — `CoreJsonContext` in the core, `SettingsJsonContext` in the server.
- **No new tool, no new setting the panel writes, no new UI.** Those are plans 2 and 4. This plan
  changes what the server *can* read and what the panel's catalog *is*, not what either does.

## What is already there (reuse-first)

- **`shared/team-server-url-vectors.json`** — a file neither half owns, asserted by both suites
  ([TeamServerUrlVectorTests.cs:27-31](../src_mcp/tests/TeamServerUrlVectorTests.cs#L27-L31) climbs
  from `tests/bin/<cfg>/net10.0` to the repository root; the TypeScript twin does the same). That
  locator is copied verbatim; the file is a sibling.
- **`RolePrompts.ForChoice(PromptChoice)`** — override-first text resolution, already keyed by the
  prompt's id ([RolePrompts.cs:30-35](../src_mcp/src/Server/RolePrompts.cs#L30-L35)). It is the one
  path a custom prompt needs; only its enum-keyed wrappers change.
- **`ParseVendors`** ([PanelSettings.cs:466](../src_mcp/src/Server/PanelSettings.cs#L466)) and
  **`ParsePromptRounds`** ([PanelSettings.cs:401-419](../src_mcp/src/Server/PanelSettings.cs#L401-L419))
  — the two "JSON in one env key, malformed means none" parsers. `COAI_ROLES` is a third, in the same
  shape, in the same file.
- **`PanelSettings.Unrecognised`** ([PanelSettings.cs:259](../src_mcp/src/Server/PanelSettings.cs#L259))
  — the list of sentences the panel already shows for a value the server could not use. What
  composition drops goes there, so a person sees *why* a role they wrote is not running.
- **A role that was not asked is named where the AI reads it** — `SkippedRole(Role, Reason)` rides
  on the round as `NotAsked` ([SessionState.cs:179, 197](../src_mcp/core/Rounds/SessionState.cs#L179))
  and reaches `ReviewerSummaryFactory.From(results, excluded, roundWork.NotAsked)`
  ([PanelService.cs:695](../src_mcp/src/Server/PanelService.cs#L695)), shipped by
  [PLAN_a_skipped_role_reaches_the_ai.md](PLAN_a_skipped_role_reaches_the_ai.md) on
  2026-09-10 for the Conventions role with no rules to read. A prompt with no text is the same shape —
  a role the round could not ask — and plugs into the same list with its own reason. The
  provider-level `ExcludedFrom` ([PanelService.cs:248-251](../src_mcp/src/Server/PanelService.cs#L248-L251))
  stays what it is: a vendor that cannot run.
- **`scripts/generate-help-prompts.mjs`** — a generator that already writes a checked-in `.ts` from
  the server's files, with a byte-equality test behind it
  ([helpPrompts.test.ts:20-27](../src_vs_code/src/test/helpPrompts.test.ts#L20-L27)). The second
  generator copies its shape; the first one starts reading the seed instead of regexing `prompts.ts`
  ([generate-help-prompts.mjs:22-30](../src_vs_code/scripts/generate-help-prompts.mjs#L22-L30)) — its
  own `ROLE_LABELS` table has no `Conventions` row today
  ([:31-36](../src_vs_code/scripts/generate-help-prompts.mjs#L31-L36)), which is the kind of drift a
  seed ends.

## The design

**1. One seed, `shared/builtin-roles.json`.** Metadata only — ids, names, stage, kind, and each role's
prompts with their labels and purposes. The prompt BODIES stay where they are, embedded `.md` files
under `src_mcp/src/prompts/` ([CoaiMcp.csproj:39](../src_mcp/src/CoaiMcp.csproj#L39) embeds them by
glob; nothing there changes). The seed's first prompt of a role is its general one — what
`PromptChoice.Universal` means today, expressed as position rather than a flag, so "exactly one general
prompt per role" is true by construction.

```json
{
  "why": "The built-in review roles, read by coai-mcp (embedded) and by the extension (generated). …",
  "roles": [
    { "id": "PlanCritique", "name": "Plan review", "stage": "plan", "programmingTask": true,
      "prompts": [
        { "id": "plan-critique", "label": "Universal", "purpose": "The whole plan: assumptions, failure paths, order, testability." },
        { "id": "plan-assumptions", "label": "Assumptions & verification", "purpose": "…" }
      ] },
    { "id": "Conventions", "name": "Conventions", "stage": "result", "programmingTask": true,
      "prompts": [ { "id": "conventions", "label": "Conventions", "purpose": "…" } ] },
    { "id": "Architecture", "name": "Architecture", "stage": "result", "programmingTask": true, "prompts": [ … ] },
    { "id": "SecurityReliability", "name": "Security & reliability", "stage": "result", "programmingTask": true, "prompts": [ … ] },
    { "id": "UxDxPerformance", "name": "Performance & UX-DX", "stage": "result", "programmingTask": true, "prompts": [ … ] }
  ]
}
```

Order is the order a round runs them and the order the panel draws them — Conventions first among the
result roles, for the reason recorded when it became a role. `stage` is `plan` | `result`; the panel
keeps saying `code` for the result stage until plan 4 gives it a reason to say otherwise.

**2. `RoleCatalog` in the core, `PromptChoice` unchanged.** New file `src_mcp/core/Rounds/RoleCatalog.cs`;
`PromptCatalog.cs` is deleted and its constants and `ForRound` move over, so call sites change a type
name and nothing else.

```csharp
public sealed record RoleDefinition(
    string Id,                       // a built-in's historical name, or a custom role's latin slug
    string Name,                     // display; free-form, any script
    string Stage,                    // RoleStages.Plan | RoleStages.Result
    bool ProgrammingTask,            // true: the result is a diff; false: a document (plan 4)
    bool Active = true,
    bool BuiltIn = false,
    IReadOnlyList<PromptChoice>? Prompts = null)   // [0] is the general prompt
{
    public IReadOnlyList<PromptChoice> Prompts { get; init; } = Prompts ?? [];
    public PromptChoice General => Prompts[0];
}

public sealed class RoleCatalog
{
    public const string PlanRole = "PlanCritique", ConventionsRole = "Conventions", …, ConventionsId = "conventions";

    /// <summary>The seed, read once from this assembly's manifest resource.</summary>
    public static RoleCatalog Builtin { get; }

    public IReadOnlyList<RoleDefinition> Roles { get; }
    /// <summary>What composition refused, each as "id: reason" — the panel shows these.</summary>
    public IReadOnlyList<string> Dropped { get; }

    public static RoleCatalog Compose(IReadOnlyList<RoleDefinition> custom);

    public RoleDefinition? ById(string id);                       // case-insensitive, like the enum parse was
    public IReadOnlyList<string> RolesOf(string stage);           // ids, in catalog order, ProgrammingTask only (plan 4 widens)
    public IEnumerable<PromptChoice> For(string roleId);          // today's PromptCatalog.For
    public PromptChoice UniversalFor(string roleId);              // today's UniversalFor: the role's general prompt
    public PromptChoice? PromptById(string promptId);             // today's ById
    public PromptChoice ForRound(string roleId, int round, IReadOnlyList<string> chosen);   // today's ForRound, verbatim
}
```

`PromptChoice(Id, Role, Label, Purpose, Universal)` keeps its shape and gains one trailing field,
`bool BuiltIn = false`; the seed loader sets it true. `RolePrompts.ForChoice(PromptChoice)`,
`ComposePrompt(PromptChoice, …)` and every test constructing a `PromptChoice` keep compiling.

`Builtin` deserialises the embedded seed through a new `[JsonSerializable(typeof(RoleSeed))]` on
`CoreJsonContext` ([CoreJsonContext.cs](../src_mcp/core/Findings/CoreJsonContext.cs)); the resource is
`<EmbeddedResource Include="..\..\shared\builtin-roles.json" LogicalName="CoaiMcp.Core.builtin-roles.json" />`
in `CoaiMcp.Core.csproj`. The Team server reaches it by the project reference it already has.

**3. `PanelConfig` reads the catalog instead of two arrays.** `AllRoles` and `CodeRoleNames`
([SessionState.cs:87-93](../src_mcp/core/Rounds/SessionState.cs#L87-L93)) are deleted;
`PanelConfig` gains `RoleCatalog Catalog { get; init; } = RoleCatalog.Builtin;` and

- `RolesOf(Stage stage)` ([:150-151](../src_mcp/core/Rounds/SessionState.cs#L150-L151)) becomes
  `Catalog.RolesOf(stage == Stage.PlanReview ? RoleStages.Plan : RoleStages.Result)`;
- `Defaults()` ([:97-98](../src_mcp/core/Rounds/SessionState.cs#L97-L98)) and the unknown-name
  fallback in `For(string)` ([:101-104](../src_mcp/core/Rounds/SessionState.cs#L101-L104)) pick
  `PlanDefault`/`CodeDefault` by the catalog's stage for that id;
- `Uniform(…)` ([:157-158](../src_mcp/core/Rounds/SessionState.cs#L157-L158)) stays, over `Builtin`.

`EnabledRolesOf`, `For(Stage)` and `RolesForRound` do not change: they never knew the names.

**4. Composition — the rules, in one pure function.** `RoleCatalog.Compose(custom)`, where `custom`
is what `ParseRoles` made of `COAI_ROLES`. Absent, empty (`[]`) and malformed all mean the same
thing — **no custom roles, the seed as shipped** — and a test says so for each of the three.

1. **The seed first, in seed order.** A custom entry whose `id` matches a built-in id
   **case-insensitively** is an OVERRIDE of that built-in, never a new role: its identity — id in
   the seed's spelling, name, stage, kind, `BuiltIn` — is never taken from user data. An override
   may contribute two things, each only when PRESENT: `active` (omitted = the built-in stays as
   shipped; the DTO field is `bool?`, because a non-nullable one would read an omitted `active` as
   `false` and silently switch Architecture off), and extra prompts (omitted = none; the DTO's
   `prompts` is nullable). A prompt whose id matches a shipped prompt id is dropped and named
   ("`arch-naming` is a shipped prompt and cannot be replaced").
2. **Then the custom roles, in the order given.** A role id must match `^[A-Za-z][A-Za-z0-9_-]*$` —
   it becomes `COAI_ROUNDS_<ID>` upper-cased, so two ids that upper-case alike are one collision,
   and the later one is dropped, named with BOTH spellings so a person sees which one it hit.
   `prompts` must be present and non-empty — a custom role has no shipped general prompt to fall
   back on; `active` omitted means true; `stage` must be `plan` or `result`. Anything else is
   dropped and named. Two entries with one id: first wins, second named — `ParseVendors`'s rule.
3. **Prompt ids are file names, so they are slugs and they are global.** Every prompt id — on an
   override or a custom role — must match `^[a-z0-9][a-z0-9-]*$`, the shape every shipped id already
   has, and be unique across the WHOLE catalog, case-insensitively: `RolePrompts` keys files by
   prompt id alone, so two roles naming `rules` would read one file. A later collision is dropped
   and named. The slug rule is what makes `../../secrets` impossible as an id; `RolePrompts`
   additionally refuses to read a resolved path that does not sit under `<dataDir>/prompts/`, so
   the rule's failure would be harmless rather than a file read that reaches a reviewer.
4. **Per bucket — `stage × programmingTask` — at most five active roles.** Built-ins are always
   first in catalog order, so the cap can never deactivate a built-in the seed or its override left
   active; it trims CUSTOM roles only, in the order given, storing each surplus one with
   `Active = false` and naming it ("over the five-active limit for the result stage"). A
   consequence worth saying out loud: with the four shipped result roles all active, ONE custom
   result role can be active — the operator makes room by unticking a built-in. This IS the
   operator's "at most five active per stage": with the kind axis, roles of the other kind never run
   in the same round, so a bucket is what a stage means, and the cost the limit exists to bound —
   launches per vendor per round — is bounded at five either way.
5. Every custom role and every custom prompt is `BuiltIn = false`. `RoleDefinition.Prompts` is
   non-nullable and never empty — the record throws `ArgumentException` on an empty list, and only
   the seed loader and `Compose` (after the checks above) ever construct one, so
   `General => Prompts[0]` is a programming error's throw, never a configuration's.

`Dropped` is appended to `PanelSettings.Unrecognised`, so `providers` answers it and the panel's
existing sentence shows it. This is the whole of the server's opinion on a hand-written `COAI_ROLES`:
nothing crashes, nothing is silently ignored, and the reason is a sentence.

**5. `PanelSettings` reads `COAI_ROLES`.** Beside `ParsePromptRounds`: `ParseRoles(json)` deserialises
a `List<RoleDefinitionDto>` — every field nullable, because absence must be distinguishable from a
value: `(string? Id, string? Name, string? Stage, bool? ProgrammingTask, bool? Active,
List<PromptDto>? Prompts)` with `PromptDto(string? Id, string? Label, string? Purpose)` — through
`SettingsJsonContext` ([SettingsJsonContext.cs:33-36](../src_mcp/src/Server/SettingsJsonContext.cs#L33-L36)),
malformed → empty. `RoleGates(env)` ([PanelSettings.cs:543-566](../src_mcp/src/Server/PanelSettings.cs#L543-L566))
takes the composed catalog and iterates `catalog.Roles`: `isPlan` is `role.Stage == plan`, the shipped
default is by stage, the env keys are as today. The one rule kept exactly: the built-in `PlanCritique`
honours no `COAI_ENABLED_` key (the operator's ruling, and nobody writes that key today); a custom role
honours its key whatever its stage — the panel-side "last one standing" guard is plan 2's.
`Rounds = new PanelConfig(gates, …) { Catalog = composed }`.

**6. The enum goes.** [ReviewerRuntime.cs:9-22](../src_mcp/runners/Reviewers/ReviewerRuntime.cs#L9-L22)
deleted; `IReviewerRuntime.Build(string role, …)` ([:129](../src_mcp/runners/Reviewers/ReviewerRuntime.cs#L129))
and its six implementations ([:221](../src_mcp/runners/Reviewers/ReviewerRuntime.cs#L221),
[:293](../src_mcp/runners/Reviewers/ReviewerRuntime.cs#L293),
[ClaudeRuntime.cs:64](../src_mcp/runners/Reviewers/ClaudeRuntime.cs#L64),
[LocalRuntime.cs:111](../src_mcp/runners/Reviewers/LocalRuntime.cs#L111),
[RemoteRuntime.cs:48](../src_mcp/runners/Reviewers/RemoteRuntime.cs#L48),
[AntigravityRuntime.cs:37](../src_mcp/runners/Reviewers/AntigravityRuntime.cs#L37));
`ReviewerInvocation.Role` ([:104-112](../src_mcp/runners/Reviewers/ReviewerRuntime.cs#L104-L112)) and
`ReviewerProgress.Role` ([BoundedScheduler.cs:43-49](../src_mcp/runners/Reviewers/BoundedScheduler.cs#L43-L49))
become `string`. The `.ToString()` calls on them are removed in the files this plan touches anyway.

In `PanelService`: the static `CodeRoles` ([:133-134](../src_mcp/src/Server/PanelService.cs#L133-L134))
and `[ReviewRole.PlanCritique]` at [:349](../src_mcp/src/Server/PanelService.cs#L349) read
`_settings.Rounds.RolesOf(...)`; the two `Enum.Parse<ReviewRole>` at
[:476](../src_mcp/src/Server/PanelService.cs#L476) and [:1176](../src_mcp/src/Server/PanelService.cs#L1176)
become the strings they already were; `ChoiceFor` ([:929-933](../src_mcp/src/Server/PanelService.cs#L929-L933))
and the `Add` closure ([:1081-1083](../src_mcp/src/Server/PanelService.cs#L1081-L1083)) call the
catalog instance; `RolesWithRulesInMind` ([:1204-1207](../src_mcp/src/Server/PanelService.cs#L1204-L1207))
compares strings. `NoCodeRolesRefusal` ([:145-148](../src_mcp/src/Server/PanelService.cs#L145-L148))
names the catalog's result-stage roles by display name instead of a hard-coded four — same sentence
for the five built-ins, right sentence for a sixth.

`RolePrompts(dataDir)` becomes `RolePrompts(dataDir, RoleCatalog catalog)`; its enum-keyed quartet
([RolePrompts.cs:24, 38, 50-63, 65-71](../src_mcp/src/Server/RolePrompts.cs#L24)) is re-keyed by
**prompt id** and `FileFor`'s switch is deleted — the file is `$"{promptId}.md"`. One new method,
`bool TryForChoice(PromptChoice choice, out string text)`: override file, else the embedded default
**only when `choice.BuiltIn`**, else false. `ForChoice` keeps throwing for a shipped prompt whose
resource is missing (that is a broken build, and the existing message says so). The `Add` closure uses
`TryForChoice`; a false answer skips the item and records
`SkippedRole(role, "prompt '{id}' has no text at <dataDir>/prompts/{id}.md and no shipped default")`
in the round's `NotAsked` — the list `ReviewerSummaryFactory.From` already receives
([PanelService.cs:695](../src_mcp/src/Server/PanelService.cs#L695)) and the calling AI already reads
as "*X was not asked: …*". One sentence per role, whatever the number of vendors that would have
carried it.

The same closure keeps a custom role away from a Team server. A `remote` vendor handed a role that
is not `BuiltIn` is skipped for that role and named in the round's excluded list —
`"{vendor}: '{role name}' is not a role this Team server accepts yet — it knows the five shipped
ones; the server side is plan 3"` — rather than launched into a request the server refuses with a
400 and the round then reports as a reviewer that failed. The refusal is the honest answer; naming
it before the launch is cheaper than reading it afterwards, and the mechanism is the one
`ExcludedFrom` already feeds.

`review_code`'s description ([Tools.cs:78-80](../src_mcp/src/Tools.cs#L78-L80)) says "three
reviewers (architecture / security+reliability / UX-DX)", which has been four roles since 2026-09-08;
it becomes "one reviewer per role configured for the result stage — Conventions, Architecture,
Security & reliability, Performance & UX-DX as shipped". A description, not behaviour.

**7. The Team server compiles, and refuses exactly as today.** `ReviewEndpoints.Refusal`
([:206-214](../src_server/src/Jobs/ReviewEndpoints.cs#L206-L214)) and `JobKinds.Refusal`
([JobKind.cs:124-126](../src_server/src/Jobs/JobKind.cs#L124-L126)) check
`RoleCatalog.Builtin.ById(role) is not null` and list `RoleCatalog.Builtin.Roles.Select(r => r.Id)` —
the same five names, the same sentence. The endpoint canonicalises the spelling to the catalog's, so
the ledger row and the answer file carry `Architecture` for a client that sent `architecture`, as the
enum parse made them today. `ReviewLauncher.RoleOf` is deleted; [:167](../src_server/src/Jobs/ReviewLauncher.cs#L167)
passes `job.Role` to `Build`. **Widening this check is plan 3.** Keeping the loosening out of the
refactor is what makes "zero behaviour change" a checkable claim here.

**8. The extension's catalog is generated.** `scripts/generate-builtin-roles.mjs` reads
`shared/builtin-roles.json` and writes `src/builtinRoles.generated.ts` — a header saying it is
generated and how, then `export const BUILTIN_ROLES: readonly RoleDefinition[] = [ … ]`. The
`RoleDefinition` interface lives in `prompts.ts`, hand-written, and `ROLES`/`PROMPTS` become
derivations of `BUILTIN_ROLES` with today's exact shapes (`stage: 'plan' | 'code'`, `universal` = is
the role's first prompt), so [panelView.ts](../src_vs_code/src/panelView.ts) and the five test files
that import them do not change. `promptsFor`, `universalFor`, `selectedFor` and the two `_SINCE`
constants are untouched. `generate-help-prompts.mjs` reads the seed for its grouping and its labels;
`helpPrompts.ts` is regenerated and its byte-equality test stays as it is.

**9. What holds the two halves level now.** Not a regex over C# source. Each half asserts its
**loader** against the seed: `BuiltinRoleCatalogTests.cs` reads `shared/builtin-roles.json` from
disk (the vectors locator) and asserts `RoleCatalog.Builtin` equals it field for field;
`builtinRoleCatalog.test.ts` reads the same file and asserts `BUILTIN_ROLES` equals it — which is
also the test that fails when someone edits the seed and forgets to regenerate. The behavioural half
of `panelServerPromptAgreement.test.ts` — an unset round shows the general prompt, an explicit choice
is shown, `conventions` belongs to its own role and no other — stays, over `BUILTIN_ROLES` rather
than over a parse of `PromptCatalog.cs`; `ConventionsPassTests`' catalog-shape facts
([:29-73, 133-160](../src_mcp/tests/ConventionsPassTests.cs#L29-L73)) stay over `RoleCatalog.Builtin`.
A seed edit that breaks a property goes red on both sides for the same reason, which is the property
the old test was for.

This is the same shape as the token-file section of `architecture.md` — an interface neither container
owns, held by both suites — and `architecture.md` gains a paragraph saying so.

## What this plan deliberately does not do

- **No CRUD tab, no `coai.roles` setting, no writer for `<dataDir>/prompts/<id>.md`** — plan 2,
  [PLAN_review_roles_crud_tab.md](PLAN_review_roles_crud_tab.md), **shipped 2026-09-12**. Until it
  landed, a custom role was a hand-written `COAI_ROLES` in the
  server's `settings.json` plus a hand-placed `.md`, which is how `COAI_VENDORS` was driven before
  the panel knew vendors. Plan 2 also owns `CUSTOM_ROLES_SINCE` (= the version this plan ships as)
  and the skew banner, because it is plan 2 that starts *writing* the key.
- **The Team server still refuses any role but the five** — plan 3,
  *PLAN_team_server_accepts_custom_roles* (`Coai:ExtraRoles`, `Coai:AllowAnyRole`).
- **No `review_document`, no session kind, no artifact** — plan 4, *PLAN_review_document*. A role
  with `programmingTask: false` is accepted and stored by this plan and **takes part in no round**,
  because `RolesOf` filters to programming roles; it is not named as dropped, because it is not
  dropped — it is waiting for the stage that runs it, and this plan's `module_server.md` entry says
  exactly that.
- **No upload of a document to a Team server** — plan 5, *PLAN_team_server_reviews_documents*.
- **No release of the Team server.** It is rebuilt by CI on the pull request and behaves identically;
  plan 3 is the one that has something to deploy.

## Build order

1. **RED tests first** (§ Test plan): `BuiltinRoleCatalogTests`, `RoleCatalogComposeTests`, the
   `RolePromptsTests` addition, the `PanelServiceTests` custom-role round, `builtinRoleCatalog.test.ts`.
   They fail to compile or fail on a missing file — watched, with the message recorded.
2. `shared/builtin-roles.json` — the 25 rows transcribed from `PromptCatalog.cs:62-93` and
   `prompts.ts:33-66` (they are the same rows; the transcription is checked by both loaders).
3. `src_mcp/core`: `RoleCatalog.cs` (record, catalog, `Compose`, the constants), `CoreJsonContext`
   gains the seed shape, `CoaiMcp.Core.csproj` embeds the file; `PromptCatalog.cs` deleted;
   `PanelConfig` reads the catalog. Core tests green.
4. `src_mcp/runners`: the enum deleted; `Build`, `ReviewerInvocation`, `ReviewerProgress` on
   `string`. Mechanical.
5. `src_mcp/src`: `PanelSettings` (`ParseRoles`, `RoleGates` over the catalog, `Dropped` into
   `Unrecognised`), `RolePrompts` re-keyed with `TryForChoice`, `PanelService` per § 6, `Tools.cs`
   description. `SettingsJsonContext` gains the DTO list.
6. `src_server`: the three readers per § 7; `RoleOf` deleted.
7. The C# test suites: `ReviewRole.X` → `RoleCatalog.X` across the ~24 test files that reference the
   enum — a rename, not a rewrite; `ConventionsPassTests` over `RoleCatalog.Builtin`. Both
   executables green with no assertion changed.
8. `src_vs_code`: `scripts/generate-builtin-roles.mjs`, the generated file, `prompts.ts` derivations,
   `generate-help-prompts.mjs` over the seed, `helpPrompts.ts` regenerated, the two tests per § 9.
   `npm test` green.
9. Docs per the acceptance ritual; the pull request.

### As epics and stories — the gate's order, and who builds what

The plan round's commands asked for 2–4 epics of 2–4 stories, each story reviewed by `review_code`
on its own diff, resolved, documented, tested and committed before the next. Three epics, eight
stories. The split was made on Fable; the model for each story is the one the same command
prescribes — Fable where being wrong is expensive (the catalog's shape, the file-path boundary),
Opus for the mechanical rest — and is named in the summary.

| Epic | Story | Builds | Model |
|---|---|---|---|
| **A — the seed and the core catalog** | A1 | `shared/builtin-roles.json`; `RoleDefinition`, `RoleCatalog.Builtin`, the seed loader on `CoreJsonContext`, the csproj embed; `BuiltinRoleCatalogTests` | Fable |
| | A2 | `Compose` with the five rules; `RoleCatalogComposeTests`; `PanelConfig` reads the catalog; `PromptCatalog.cs` deleted; the core suite green | Fable |
| **B — the enum goes** | B1 | the runners on `string` (`Build`, `ReviewerInvocation`, `ReviewerProgress`); `PanelService` per § 6 except the prompt-text branch; the mechanical renames across the test files; both executables compile and are green | Opus |
| | B2 | `ParseRoles`, `RoleGates` over the catalog, `Dropped` → `Unrecognised`; `RolePrompts` by prompt id, `TryForChoice`, the path check (RED first); `NotAsked` for a prompt with no text; the remote-vendor exclusion; the fake-CLI tests | Fable |
| | B3 | the Team server's three readers over `RoleCatalog.Builtin`, `RoleOf` deleted, canonical spelling; `CoaiServer.Tests` green plus the one addition; `Tools.cs` description | Opus |
| **C — the extension's catalog is generated** | C1 | `generate-builtin-roles.mjs`, `builtinRoles.generated.ts`, `prompts.ts` as derivations; `builtinRoleCatalog.test.ts` | Opus |
| | C2 | `panelServerPromptAgreement.test.ts` over the seed; `generate-help-prompts.mjs` over the seed; `helpPrompts.ts` regenerated; `npm test` green | Opus |
| | C3 | the module docs, `architecture.md`'s paragraph, the CHANGELOG line, `coai-mcp` at `0.19.0`; the pull request and its automatic comments read | Opus |

## Test plan

Every new behaviour has a test that was watched failing first. The characterization net is the
existing suites, unedited beyond renames.

**C#, `CoaiMcp.Tests.exe`** (never `dotnet test`):

- `BuiltinRoleCatalogTests`
  - `TheEmbeddedSeed_IsTheFileInShared_FieldForField` — the vectors locator; deep equality.
  - `TheCatalog_IsTodaysFiveRolesAndTwentyFivePrompts_InTodaysOrder` — ids and order pinned; this
    is the characterization of what `PromptCatalog.All` was.
  - `EveryRole_HasItsGeneralPromptFirst_AndEveryPromptIdIsUnique`.
  - `EveryShippedPrompt_IsEmbeddedInTheBinary` — over the seed, replacing the fixed list
    `RolePromptsTests` walks today.
- `RoleCatalogComposeTests` (pure)
  - `NoCustomRoles_AnEmptyArray_AndMalformedJson_EachComposeToExactlyTheBuiltins` — `[Theory]`,
    three inputs, one answer.
  - `ACustomEntryWithABuiltinId_KeepsTheBuiltinIdentity_AndGainsOnlyItsExtraPrompts` — name, stage,
    kind from the seed; the extra prompts from the entry.
  - `AnOverrideSpelledInAnotherCase_IsTheBuiltin_NotANewRole` — `architecture` overrides
    `Architecture`; the catalog still has five roles.
  - `AnOverrideThatOmitsActive_LeavesTheBuiltinAsShipped_AndOneThatSaysFalse_SwitchesItOff` — the
    presence rule, both directions.
  - `ACustomPromptNamedLikeAShippedOne_IsDroppedAndNamed`.
  - `APromptIdThatIsNotASlug_IsDroppedAndNamed` — `../../secrets`, `Rules`, `a b`.
  - `APromptIdUsedByTwoRoles_IsKeptOnTheFirst_AndDroppedAndNamedOnTheSecond` — case-insensitive.
  - `ASixthActiveRoleInABucket_IsStoredInactive_AndNamed_AndNoBuiltinIsEverTrimmed` — with all
    four result built-ins active, the second custom result role is the one trimmed.
  - `AnIdThatIsNotLatin_OrCollidesUpperCased_OrIsRepeated_IsDroppedAndNamedWithBothSpellings` —
    three cases, `[Theory]`.
  - `ACustomRoleWithNoPrompts_IsDroppedAndNamed` — and `RoleDefinition` itself throws on an empty
    list, so the drop happens before construction.
  - `ARoleMarkedNotProgramming_IsInTheCatalog_AndInNoRound` — `RolesOf` on both stages omits it.
- `SettingsReachTheServerTests` additions: `COAI_ROLES` reaches `Rounds.Catalog`; malformed JSON is
  the built-in catalog with nothing in `Unrecognised` (matching `COAI_VENDORS`); a dropped entry's
  sentence is in `Unrecognised`.
- `RolePromptsTests`: `ACustomPrompt_ReadsItsOverrideFile_AndNeverAsksTheAssembly` — **RED first
  against today's code**: `Embedded("my-prompt.md")` throws `InvalidOperationException` naming the
  csproj, which is the defect class (a custom id routed to the embedded lookup). GREEN: `TryForChoice`
  false with no file, true with one. `OverrideWins_AndRestoreBringsTheShippedTextBack` re-keyed by
  prompt id, unchanged in substance. `APathOutsideThePromptsDirectory_IsNeverRead` — **RED first
  against today's code**: `Path.Combine(OverrideDir, "../outside.md")` reads a file placed one
  level up; GREEN: the resolved path is checked against the directory and the read is refused.
- `PanelServiceTests` (fake CLI): `ACustomRoleInCOAI_ROLES_IsDealtWorkLikeAShippedOne` — the built
  work contains `fake/MyRole` with the override file's text as its prompt;
  `ACustomRoleWhosePromptHasNoFile_IsNamedAsNotAsked_AndTheRoundStillRuns` — the `SkippedRole`
  appears in the round summary's `NotAsked` with its reason, the other reviewers ran;
  `ACustomRoleOnARemoteVendor_IsNamedAsExcluded_AndNeverSent` — a `remote` row in the vendor list,
  a custom role in the catalog: no request leaves, the excluded list carries the sentence, the CLI
  vendor for the same role still ran.
- `ConventionsPassTests`, `PromptCatalogTests` (renamed), `RoleGateTests`, `StageGateTests`,
  `GateReportingTests`, `VendorStagesTests`, `ReviewerLaunchTests`, `McpContractTests` and the rest:
  green with the enum-to-constant rename only.

**C#, `CoaiServer.Tests.exe`**: `ReviewEndpointTests` green unedited — an unknown role is still refused,
and the refusal lists the same five names; `JobKindTests` green unedited. One addition:
`TheRoleReachesTheAdapter_AsTheCatalogSpellsIt` — a job sent as `architecture` launches with
`Architecture`, which is what the enum parse did.

**TypeScript, `npm test` in `src_vs_code`**:

- `builtinRoleCatalog.test.ts` — `the generated catalog is the shared seed, field for field` (the
  forgot-to-regenerate guard); `ROLES and PROMPTS derive the shapes panelView reads` (five roles,
  `stage` values, one `universal` per role).
- `panelServerPromptAgreement.test.ts` — the two regex readers removed; the three behavioural tests
  kept over `BUILTIN_ROLES`.
- `helpPrompts.test.ts`, `panelServerDefaultsAgreement.test.ts`, `settingsShape.test.ts`,
  `settingWrite.test.ts`, `settingsReach.test.ts`, `panelView.test.ts`: green unedited.

## Version skew

- **`coai-mcp` ships as `mcp-v0.19.0`** — a new settings key is a minor bump. An older server never
  reads `COAI_ROLES`; the key is invisible to it by construction, because `Unrecognised` reports
  unknown *values of known keys* and nothing else. Nobody writes the key until plan 2, and plan 2
  carries the banner (`CUSTOM_ROLES_SINCE = '0.19.0'`, the `ROLE_SWITCH_SINCE` shape).
- **The extension** carries a generated file and regenerated help with no visible change; it ships
  with the next release cut for any reason, under a CHANGELOG line that says what moved and why.
- **The Team server** is unchanged in behaviour and not released by this plan.

## Acceptance — one PR, and the gate on both sides of it

This plan ships as **its own branch (`feat/review-roles-become-data`) and its own pull request**, accepted
only when the whole ritual has run.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file; `resolve` every finding (a rejection carries a reason);
   repeat until `proceed`.
2. **Tests first**, per the test plan — RED watched, GREEN after; whole suites on both sides, both
   executables and `npm test`.
3. **Gate, after the code.** `review_code` with the scope = *What must be true* + *Constraints* +
   the Definition of Done, and the diff `main...feat/review-roles-become-data` — three dots.
4. **Documentation.** `research/module_core.md` (the catalog as an entity, the compose rules),
   `research/module_server.md` (`COAI_ROLES`, `RolePrompts` by prompt id, a prompt with no text is
   a named exclusion, a document role waits for plan 4), `research/module_extension.md` (the
   generated catalog and what the agreement test now asserts), `research/module_team_server.md`
   (the role check reads the shared catalog), `research/architecture.md` (the seed as an interface
   neither half owns, beside the token file and the URL vectors).
5. **Help.** No new command, no new setting — `helpCoverage.test.ts` is unaffected; said here so
   nobody looks for the missing article.
6. **CHANGELOG.** `src_vs_code/CHANGELOG.md`, one paragraph in the file's own voice: the role
   catalog is generated from one shared file, and nothing a person sees changed.
7. **Manifest.** `coai-mcp` version `0.19.0` for the release line; the extension's version is not
   bumped by this plan.
8. **Family checks.** `plan-lifecycle.mjs` and `pin-check.mjs` clean.
9. **Promote.** On merge, `/promote-plan` this file with `IMPLEMENTED <date>` and every deviation
   recorded. This promotion is also when the family's subagent-model rule is committed to
   `dew_flow_conventions`, so the pin cascade runs once for both.

## Definition of Done

- [ ] `shared/builtin-roles.json` exists; `RoleCatalog.Builtin` and `BUILTIN_ROLES` each equal it, by a test that reads the file.
- [ ] `enum ReviewRole` is gone; a role is a `string` from `Build` to the Team server's endpoint; `ReviewLauncher.RoleOf` is deleted.
- [ ] `COAI_ROLES` is parsed, composed by the five rules, and what composition drops is a sentence in `Unrecognised`; absent, `[]` and malformed all give the seed as shipped.
- [ ] An override is presence-aware: an omitted `active` leaves the built-in as shipped; a mis-cased built-in id is an override, not a new role.
- [ ] Prompt ids are slugs and globally unique; `RolePrompts` never reads a path outside `<dataDir>/prompts/` — proven RED first.
- [ ] A custom role with its text on disk is dealt work in a fake-CLI round; one without text is named in `NotAsked` with its reason and the round runs; a `remote` vendor is named as excluded for a custom role and never sent.
- [ ] A role marked `programmingTask: false` is stored and runs in no round, and the module doc says why.
- [ ] Both C# suites and `npm test` pass with only mechanical renames in existing tests; every new test was watched RED first.
- [ ] `panelServerPromptAgreement.test.ts` no longer reads `PromptCatalog.cs`; `generate-help-prompts.mjs` no longer regexes `prompts.ts`.
- [ ] The Team server refuses an unknown role with the same sentence and the same five names as before.
- [ ] Docs, CHANGELOG, version and family checks per the ritual; the gate reached `proceed` on the plan and on the code.

## Parallelism

**Runs alone.** This is the widest-blast-radius slice of the five: `PanelSettings.cs`,
`SessionState.cs`, `RolePrompts.cs`, `PanelService.cs`, `ReviewerRuntime.cs` and the four runtime
files, `Tools.cs`, the Team server's `Jobs/`, `prompts.ts`, and the C# test files that name the
enum. One open plan names the same file and must not be built at the same time as this one:
[PLAN_provider_liveness.md](../todo/PLAN_provider_liveness.md) (`PanelService.cs`).
[PLAN_the_log_names_the_model.md](../todo/PLAN_the_log_names_the_model.md) touches `ReviewerState`, which
this plan reads but does not change — a textual conflict at worst.

The four plans that follow — the CRUD tab, the Team server's role allowlist, `review_document`, the
Team server's document upload — each branch from this one once it is on `main`; the first three are
independent of each other and may run in parallel; the fourth needs two of them.

## What shipped differently

Recorded at promotion, because what a plan got WRONG is the most useful part of the record.

- **A role id carries no hyphen.** The plan wrote `^[A-Za-z][A-Za-z0-9_-]*$`; the shipped rule is
  `^[A-Za-z][A-Za-z0-9_]*$`. gemini's plan round pointed out that the id becomes
  `COAI_ROUNDS_<ID>`, and `COAI_ROUNDS_MY-ROLE` is not a name a POSIX shell can export — so a role
  whose budget could be set through the settings file and never through the environment would be a
  role that works in one of the two places its settings come from.
- **`PanelConfig.AllRoles` and `CodeRoleNames` were not deleted.** They are PROJECTIONS of
  `RoleCatalog.Builtin` now, because every caller of them means what they still mean: a default that
  predates custom roles, or a sentence naming the boxes somebody sees out of the box. The roles a
  ROUND runs come from `Catalog`, which may carry a person's own.
- **`PromptCatalog` survived epic A and died in story C2.** The plan had it deleted in A2; it stayed
  as the characterization oracle `BuiltinRoleCatalogTests` held the seed against, which is what
  caught a transcription slip being impossible rather than merely unlikely. It went when the
  extension stopped mirroring it, and the twenty-five prompt ids are pinned as literals in its place.
- **The composition rules are five, not four.** The plan named four; global prompt-id uniqueness
  became the fifth, because a prompt id is the file name under `<dataDir>/prompts/` and two roles
  claiming one id would be two roles sharing one override.
- **The plan stage's roster came from the catalog too.** Not in the plan at all: `ReviewPlanAsync`
  held a hardcoded `[PlanRole]`, so a plan-stage role a person added was composed, given a budget and
  an enable switch, and then never asked anything. Found on story B2's code round, with the dealt-lens
  ownership bug and the empty-roster refusal behind it.
- **`review_document` is untouched, as planned**, and a role stored with `programmingTask: false` is
  accepted today and takes part in no round — waiting for plan 4 rather than dropped.

## The open tail, carried into plan 2

Recorded here so promotion moves it rather than losing it. Neither item blocks this plan.

- **`PanelService.BuildWork` takes eight parameters**, which this repository's own
  `StageRun` comment calls the point where a signature stops being readable — and SonarCloud
  names it on the pull request. The count PREDATES this branch: nothing here added a parameter,
  and the analyser reports it as new only because the lines moved. The fix is the one `StageRun`
  already demonstrates: group `seed`, `planPrompts` and `deal` into a record, since they are one
  answer to "how is this round dealt" rather than three independent knobs. Left undone because a
  signature change across a dozen call sites, made after the last gate round closed, is exactly
  the change nothing would have reviewed.
- **Two test methods compile a regular expression at run time** where `[GeneratedRegex]` would do
  it at build time (`BuiltinRoleCatalogTests`, `RolePromptsTests`). Declined here: the attribute
  needs its class to be `partial`, which is a change to two test classes for a pattern each of them
  matches once.

Two of SonarCloud's suggestions are refused rather than deferred: returning `List<T>` instead of
`IReadOnlyList<T>` "for improved performance" contradicts doctrine 7, and an allocation nobody has
measured is not a reason to widen a boundary type.
