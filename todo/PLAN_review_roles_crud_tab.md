# PLAN — the roles page, where a person writes a review role (2 of 5)

> Status: **plan only, nothing implemented yet, 2026-09-12.** Scope: `src_vs_code` almost entirely —
> a new page module and its host, `settingsShape.ts` (`coai.roles` and `COAI_ROLES`), `panelView.ts`
> (the button, the plan-stage switches, the custom roles in *Prompts per round*, the skew banner),
> `prompts.ts` (`CUSTOM_ROLES_SINCE`), `package.json`, the help, and the tests for all of it. One
> file on the server side: nothing, if the design below holds.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_server.md](../research/module_server.md),
> [architecture.md](../research/architecture.md);
> plan 1 of 5, now shipped: [PLAN_review_roles_become_data.md](../research/PLAN_review_roles_become_data.md).

## The symptom

`coai-mcp 0.19.0` reads `COAI_ROLES` and composes a person's own review roles onto the shipped five.
**Nothing writes it.** A person who wants the gate to check a CV, a product description or a set of
requirements has to hand-write JSON into `<dataDir>/settings.json` and hand-place a markdown file
next to it — which is how `COAI_VENDORS` was driven before the panel knew about vendors, and the
reason the panel learned.

| What a person can do today | What it takes |
|---|---|
| Add a role of their own | Hand-write a JSON array into a file the panel overwrites |
| Give it a prompt | Create `<dataDir>/prompts/<id>.md` by hand, with the right slug |
| See whether it worked | Open a round and count reviewers |
| Edit a shipped prompt's text | Same, and nothing in the product says the file is read |
| Switch a plan role off | Impossible: the plan stage has no checkbox at all |

The catalog is data and the panel still draws five constants.

## What this plan is NOT

- **No export or import of a role set.** The operator asked for it and deferred it in the same
  sentence: *"нужно, сейчас не делаем, это след версия"*. A role set is JSON in a setting, so a
  person can already copy one between machines; the button that does it politely is a later plan.
- **No Team-server change.** It still accepts the five it was compiled with, and `coai-mcp` excludes a
  custom role per (vendor, role) before the launch. Widening it is plan 3,
  *PLAN_team_server_accepts_custom_roles* (`Coai:ExtraRoles`, `Coai:AllowAnyRole`).
- **No `review_document`, no artifact, no second CLAUDE.md snippet.** A role stored with
  `programmingTask: false` is saved by this plan, shown in the page, and takes part in **no round** —
  exactly as plan 1 left it. The stage that runs one is plan 4.
- **No change to what a round OUTPUTS.** Same findings, same categories, same verdict. A custom role
  differs only in the question it asks, and that question is its prompt.

## The decisions worth arguing with

**1. A page, not a section.** The sidebar is one `<details>` accordion of eleven sections
([panelView.ts:261-273](../src_vs_code/src/panelView.ts#L261-L273)) and everything in it is short. A
role has a name, a stage, a kind, a switch and an unbounded list of prompts each with a name and a
BODY of text — a textarea per prompt, five prompts per role, five roles. That is a page, and the
operator said so: *"отдельный таб как на chat other ais - edit preset"*. So the shape is the one this
extension already has three times — a pure page module and a thin panel host
([chatPresetsPage.ts](../src_vs_code/src/chatPresetsPage.ts) +
[chatPresetsPanel.ts](../src_vs_code/src/chatPresetsPanel.ts), the rounds log, the help page). The
sidebar gains one button, in *Prompts per round*, beside the roles it opens.

**2. The setting is one JSON array, and it is the wire format.** `coai.roles` holds exactly the rows
`COAI_ROLES` carries — id, name, stage, programmingTask, active, prompts[{id,label,purpose}] — so
`envBlock` is `JSON.stringify(settings.roles)` and nothing translates. The alternative, a panel-shaped
model normalised on the way out, gives the two halves two schemas to keep level, which is the defect
plan 1 spent eight stories removing.

**3. A built-in role is a row like any other, and composition already knows.** The page shows all five
shipped roles beside a person's own. Editing one writes a row naming its id, and `RoleComposition`
treats a row naming a built-in as an OVERRIDE — every field it omits is left as shipped, which is why
every field of `RoleEntry` but the id is nullable. So the page writes only what was changed:

| On a built-in the person may | Because |
|---|---|
| change the prompt TEXT | it is the override layer `RolePrompts` already reads |
| add a prompt | the row's `prompts` list is appended to, never replaced |
| switch it off (`active: false`) | the catalog's own switch, shipped in plan 1 |
| **not** rename it | the id keys settings, session files and every rounds-database row |
| **not** delete a shipped prompt | its text is embedded; there would be nothing to restore |
| **not** delete the role | same |

The page enforces this by rendering the id read-only and the shipped prompts without a Remove button.
`RoleComposition` enforces it again, because a page is not a boundary.

**4. Prompt TEXT is a FILE, not a setting.** The body of a prompt goes to
`<dataDir>/prompts/<id>.md`, which is where `RolePrompts` has read overrides since before roles were
data — `Has`, `Text` and `FileToWrite` all name it, and `Override`/`RestoreDefault` on the C# side
already write it and are tested. The extension writes the same path itself, as it already writes the
Team-server token file into the same directory: this is the *one interface neither container owns*
pattern that [architecture.md](../research/architecture.md) records twice. Keeping the body OUT of the
setting matters for a reason a person feels: twenty-five prompts of prose in `settings.json` would be
copied into every `mcpServers` block and every settings mirror, and VS Code's settings UI would show
a person a wall of JSON with their prompts inside it.

*The id rule is the same rule on both sides.* `^[a-z0-9][a-z0-9-]*$`, no Windows device name, unique
across the whole catalog — the page GENERATES the id from the name (slugified, deduplicated) and
never asks for one, because an id is a file name and a person should not have to know that.

**5. The id is generated, the name is free.** The operator: *"технический id (латиница, генерируется)
+ отображаемое name"*. `Требования` becomes `Requirements` only if somebody transliterates it, which
nobody should have to — so the generator makes a latin id from the name where it can
(`Requirements we wrote` → `RequirementsWeWrote`) and falls back to `Role2`, `Role3` when it cannot
(a name in Cyrillic, in Chinese, or already taken). **The id is fixed at creation and never changes
afterwards**, however the name is edited, because by then it may key a settings value, a session file
and a database row. The page shows it, small and grey, under the name.

**6. Five active per stage, and the page refuses the sixth rather than silently dropping it.**
`RoleComposition` caps at five and names what it capped, so the server is safe either way; a page
that let a person tick a sixth and then showed them five would be a page that lies. The sixth tick is
`disabled` with the reason beside it, the way the last code role's untick already is
([panelView.ts:1164](../src_vs_code/src/panelView.ts#L1164)).

**7. The plan stage gets switches, and the last one cannot go off.** Plan 1 made this possible and
left it unreachable: a plan-stage role a person adds honours `COAI_ENABLED_<ID>`, and `review_plan`
refuses a round with an empty roster. The panel draws no checkbox on a plan role at all
([panelView.ts:1157-1159](../src_vs_code/src/panelView.ts#L1157-L1159)). It draws one now, with the
same last-one-disabled rule the code stage has — and the shipped `PlanCritique` keeps its carve-out:
it honours no `COAI_ENABLED_` key, so its switch writes `active` in `coai.roles` instead.

**8. Per-side follows the switch that is already there.** `roles` joins `OVERLAID_SETTINGS`
([settingsShape.ts:222-226](../src_vs_code/src/settingsShape.ts#L222-L226)), beside `rounds`,
`thresholds`, `roleEnabled` and `promptsPerRound` — a person's roles belong to the work, which is what
a side is. The prompt FILES do not: they live in one data directory, and two sides sharing a prompt
body is right, because the body is the text of a question rather than a configuration.

**9. `CUSTOM_ROLES_SINCE = '0.19.0'`, and the banner is the third of its kind.** The moment this page
can write the key, a panel newer than its server becomes possible, and an older `coai-mcp` never reads
`COAI_ROLES` — the roles appear in the panel and never run. Silent, and in the quiet direction, which
is exactly what `CONVENTIONS_ROLE_SINCE` and `ROLE_SWITCH_SINCE` exist for. The banner follows their
shape exactly ([panelView.ts:1103-1134](../src_vs_code/src/panelView.ts#L1103-L1134)): a
`<div class="stale">`, shown only when the server is KNOWN and strictly older, naming the installed
version and the roles that will not run.

**10. The page saves as you type, like its neighbour.** No Save button, no dirty state, no
confirmation — `chatPresetsPage` established it and the lead line says so out loud: *"Everything here
is saved as you type."* Removing a role asks first, because removing a role a round has already used
loses a prompt body.

## Build order

1. **The setting exists and reaches the server.** `CoaiSettings.roles`, `DEFAULTS.roles = []`, the
   `coai.roles` declaration in `package.json`, `OVERLAID_SETTINGS`, and `envBlock` emitting
   `COAI_ROLES` only when the array is non-empty. `settingsReach.test.ts` fails to compile until the
   field is in its `CHANGED` map, which is the point of that test.
2. **The role model, pure.** `roles.ts`: the `RoleRow` type, `rolesFrom(unknown)` dropping a bad row
   rather than throwing (the house rule, `chatPresets.ts:13-16`), `idFor(name, taken)` generating and
   deduplicating, `withPrompt`, `withoutPrompt`, `renamed`, `toggled`, and the five rules the page
   must enforce locally so it never offers an action the server would refuse.
3. **The page module.** `rolesPage.ts`: `rolesHtml(state, nonce)`, the `RolesCommand` union, and
   `roleEdit(message): RolesCommand` with a per-field whitelist — the shape
   `chatPresetsPage.ts:115-155` uses, and for the same reason: `__proto__` must not be a field.
4. **The host.** `rolesPanel.ts`: `openRoles()`, `apply(command)`, the prompt-body writer under
   `<dataDir>/prompts/`, and `render()`. The command `coai.editRoles`, its `PANEL_COMMANDS` entry and
   its button in *Prompts per round*.
5. **The sidebar catches up.** *Prompts per round* draws custom roles: `ROLE_TONE` gains a fallback
   that is already there (`?? 'plan'`), a role's label comes from its name, plan roles get switches,
   and the sixth active role is refused with a reason.
6. **The banner.** `CUSTOM_ROLES_SINCE`, `customRolesSkew(server, settings)`, and its place in the
   section.
7. **Help.** One article, tooltips for every new control. `helpCoverage.test.ts` and
   `helpTooltips.test.ts` both fail until this is done, in both directions.
8. **Docs and the pull request.** `module_extension.md` gains the page;
   `module_server.md`'s `COAI_ROLES` paragraph gains "and the panel writes it now"; the CHANGELOG
   gets the paragraph a person reads.

## Test plan

Every new behaviour gets a test watched RED first. The house style is pure functions and markup
assertions; `panelProvider.ts` and the new host have no unit tests, and the structural scans
(`panelsAreSearchable`, `theLogRefusesToOpen`, `settingsAreDeclared`, `liveRepaint`) cover the host's
obligations instead — all four will fail on a page that forgets one.

| What | Where | The assertion |
|---|---|---|
| A row a person wrote survives a round trip | `roles.test.ts` | `rolesFrom(JSON.parse(JSON.stringify(rows)))` equals `rows` |
| A bad row is dropped, not thrown on | `roles.test.ts` | a row with no id, a null, a string; the others survive |
| An id is generated, latin, and unique | `roles.test.ts` | two roles named the same get different ids; a Cyrillic name still gets one |
| An id never changes when the name does | `roles.test.ts` | rename, assert the id |
| A built-in cannot be renamed or deleted | `rolesPage.test.ts` | the id input is `readonly`; no Remove button on a shipped prompt |
| The sixth active role in a stage is refused | `rolesPage.test.ts` | the checkbox is `disabled` and the reason is beside it |
| The page escapes what a person typed | `rolesPage.test.ts` | `<img src=x onerror=…>` as a role name renders escaped |
| Every command the page posts is parsed | `rolesPage.test.ts` | `roleEdit` over each shape, and `__proto__` as a field is ignored |
| The setting reaches the server | `settingsReach.test.ts` | changing `roles` alone changes `envBlock` |
| `COAI_ROLES` is absent when there are no roles | `settingsReach.test.ts` | pristine settings produce no key |
| A custom role is drawn in Prompts per round | `panelView.test.ts` | its name, its pickers, its tone class |
| A plan role has a switch and the last cannot go off | `panelView.test.ts` | `disabled` on the last, with the hint |
| The banner appears only for an older KNOWN server | `panelView.test.ts` | 0.18.17 yes, 0.19.0 no, absent no, unknown no |
| A prompt body is written where the server reads it | `rolesPrompts.test.ts` | the path equals `<dataDir>/prompts/<id>.md` |
| An id that is not a slug never becomes a path | `rolesPrompts.test.ts` | `../../escaped` refused before any write |

**The characterization net**: `npm test` with `out/` removed, both C# suites unchanged. A person with
no `coai.roles` must see byte-identical HTML — `panelView.test.ts` asserts the five shipped roles by
name and the absence of any new markup.

## Version skew

- **The extension ships as a minor bump**, the release it lands in. It is the half that changes.
- **`coai-mcp` is unchanged by this plan.** 0.19.0 already reads everything this page writes; that was
  the whole point of shipping it first, while it was a no-op.
- **An older `coai-mcp` is the case the banner is for.** Below 0.19.0 the key is never read: the roles
  are in the panel and in no round. Nothing fails, nothing errors — which is why it is said out loud.
- **The Team server is untouched**, and a custom role is excluded per (vendor, role) before the launch
  by 0.19.0, so a person with a Team server sees their shipped roles run and their own named as
  excluded. Plan 3 removes that.

## Acceptance

Its own branch, its own pull request, gated on both sides — `open`, `review_plan` with this file,
`resolve` every finding, build, `review_code` over the diff, `resolve`, and the whole ritual:
documentation in the same task, the CHANGELOG paragraph, `plan-lifecycle.mjs` and `pin-check.mjs`
clean, the automatic comments read and answered, and `/promote-plan` on merge.

## Definition of Done

- [ ] A person can add a review role from the panel, name it in their own language, give it a general prompt and more prompts, switch it on and off, and never see an id they have to invent.
- [ ] The five shipped roles are on the same page: their prompt text editable, extra prompts addable, their ids and their shipped prompts untouchable.
- [ ] `coai.roles` is declared, per-side, and reaches the server as `COAI_ROLES`; a pristine configuration emits no key.
- [ ] A prompt body is written to `<dataDir>/prompts/<id>.md` and read by the round that runs — verified end to end against an installed `coai-mcp`, not only in a test.
- [ ] The plan stage has switches, and the last one on cannot be switched off.
- [ ] The sixth active role in a stage is refused in the page, with the reason visible.
- [ ] An older `coai-mcp` produces the banner; 0.19.0 and later produce nothing.
- [ ] `programmingTask: false` is storable, shown, and takes part in no round — and the page says why.
- [ ] `npm test` green with `out/` removed; both C# suites green and unedited.
- [ ] Every new control has a tooltip and the page has a help article, in English.

## What the code round changed, recorded

The gate's code round returned 34 gating findings over this branch. Five of them were one defect seen
from different directions and two were worth the whole round:

- **`composed()` applied a row's `name`, `stage` and `programmingTask` to a role this product ships.**
  `RoleComposition.Overridden` takes only `Active` and the extra prompts. Nothing failed — the page
  drew the person's word for the role, the round used the shipped one, and the two never met. The
  page now renders those three controls read-only, `composed()` mirrors the server, and `rolesEdit`
  refuses the command as well.
- **"Remove this role" did nothing**, because the host's mutation used `undefined` for both "refused"
  and "nothing changed". Doctrine 4, exactly.
- **`rolesEdit.ts` is new, and is the answer to how that survived.** Every row decision moved out of
  the untested host into a pure module returning a three-way union, with the prompt-override files to
  delete carried on the success case. The plan's *Constraints* said the host has no unit test
  anywhere and the structural scans cover its obligations; that was true and it was not enough.
- **`enabledCodeRoles` is the one count of "which code roles will run".** There were three.
- **`coai.roles` is per-side and was written globally.** `sideConfig.saveSetting` is now the single
  write path, shared with the panel.
- Smaller: commands serialized and text fields settled rather than a write per keystroke; the prompt
  body written and renamed rather than truncated in place; removal confirmed and its files deleted; a
  prompt id from the webview checked against the role that claims it; unknown fields carried through
  `rolesFrom` instead of deleted on the next keystroke; a length cap on a generated id; the nonce from
  `crypto`; the sidebar's tick inert for a role the catalog has switched off; and the page saying so
  when it has not yet learned which server is installed.

Eighteen findings were rejected with reasons, the largest group being four claims of script injection
through `JSON.stringify` inside a `<script>` — there is none in the file, and no page state reaches
the script block at all.

## Parallelism

Stories 1–2 are one unit — the model and the setting, no UI. Story 3–4 are the page and cannot start
before 2. Stories 5–6 touch `panelView.ts` only and can run beside 3–4 if two lanes are wanted; they
conflict in one file, so one lane is simpler. Story 7–8 close it.
