# PLAN — a reviewer's colour must never be a colour another reviewer already has

> Status: **IMPLEMENTED, 2026-09-08.** Scope: `src_vs_code/src/vendorColour.ts`, its four call sites
> (`panelView.ts`, `roundsLog.ts`), `PanelProvider.vendorIds()`, the manifest's new
> `contributes.colors`, and the tests for all of it.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_reviewer_card_colours.md](PLAN_reviewer_card_colours.md) — the previous day's plan, whose
> six-colour palette this replaces.

## The symptom

The operator, 2026-09-08, over a screenshot of **REVIEWERS** with six reviewers configured:

> yesterday I asked here for each agent in its own colour — but the colours must not repeat

*(translated from the operator's Russian; this repository's documentation is English.)*

```
codex                    var(--vscode-charts-blue)
gemini                   var(--vscode-charts-green)
local                    var(--vscode-charts-orange)
remsoftdev-claude        var(--vscode-charts-purple)
remsoftdev-codex         var(--vscode-charts-orange)   <-- same edge as local
remsoftdev-antigravity   var(--vscode-charts-yellow)
```

Reproduced as a failing test before anything was changed: `5 !== 6`, naming the pair.

Not an unlucky hash. `vendorColour(name)` hashed the name (FNV-1a) into SIX `--vscode-charts-*`
colours with three names anchored. Six names into six buckets collide more often than they do not,
and the same function put `claude` and `antigravity` — two shipped vendor kinds — both on purple. A
colour derived from ONE name has no way to know what the other five took, so no re-hashing could
have made this promise.

## What shipped

**Twelve contributed colours.** `contributes.colors` declares `coai.vendorColour1..12`, each with
`dark`, `light`, `highContrast` and `highContrastLight`. The hues are the twelve already tuned in
CredsForDevs (`src_vs_code/src/depColors.ts` and its manifest) — Blue, Amber, Red, Cyan, Green, Pink,
Purple, Brown, Turquoise, Lime, Orange, Slate — reused rather than re-picked, because they are known
to stay apart from each other in a light theme as well as a dark one. Rendering writes
`var(--vscode-coai-vendorColour7, #B482F5)`: theme variable first, the hex only as the fallback for a
build older than the manifest that declares the id.

**The promise moved from the name to the list.**

```ts
export type VendorPalette = (vendor: string) => string;
export function vendorPalette(configured: readonly string[]): VendorPalette;
```

- names are normalised, de-duplicated and **sorted** — sorting, not arrival order, is what makes the
  answer identical in every window and after a restart;
- the five anchored kinds hold reserved slots — `codex` blue, `gemini` green, `local` orange,
  `claude` purple, `antigravity` cyan;
- every other name asks for the slot its hash prefers and takes the next free one when that is
  spoken for;
- reserved slots are offered last, so eight strangers with no anchor configured still get eight
  distinct colours rather than repeating while blue sits unused;
- past twelve reviewers the thirteenth repeats, deliberately.

**One canonical list.** Each of the four drawing sites builds the palette once per render from the
CONFIGURED reviewer ids: `reviewersBody` and `usageRegion` from the vendor list they already hold,
`roundsBody` and `rowsFrom` from a new defaulted `vendorIds` parameter fed by
`PanelProvider.vendorIds()`. A provider that is no longer configured is still coloured, from its own
name, and that is the only colour allowed to coincide with a live reviewer's.

## What the gate changed

Three reviewers, `good_enough`, fourteen findings; eleven accepted, two rejected with reasons, one
answered by precedent. The plan as written would have shipped three real defects:

1. **The universe was inferred per render** — `configured ∪ providers seen in the data`. All three
   reviewers independently pointed out that a panel and a rounds log holding different data would
   then build different lists and paint one vendor two colours. Fixed by making the CONFIGURED list
   the single source and giving strays a name-only fallback instead.
2. **Anchors were to be "placed first"**, which does not reserve anything: with `codex` absent, a
   stranger could take blue and `codex` would return to an occupied colour. Fixed by reserving every
   anchor slot unconditionally, and lending them out only as the last resort.
3. **The manifest-to-CSS-variable spelling was assumed**, not verified. Answered by precedent —
   CredsForDevs has shipped `var(--vscode-credSshManager-depColor3)` against
   `credSshManager.depColor3` since its palette landed — and pinned by a test that reads the manifest
   and asserts every palette entry names a declared id with all four theme defaults.

Rejected: that a vendor's colour should never move when another vendor joins (the operator was asked
this exact question before the plan was written and chose the guarantee; reserving anchor slots
limits the movement to unanchored vendors that genuinely collide), and that the thirteenth reviewer
should get a fixed grey (indistinguishable from the uncoloured foreground, which costs more than the
repeat does).

## The open tail

- Nothing chooses colours *for legibility against each other* — the twelve are known-distinct by
  construction, not measured per theme. If somebody ships a theme that flattens two of them, the
  answer is a different hue in the manifest, not a change here.
- A thirteenth configured reviewer repeats a colour. Raising the palette means adding contributed
  ids; nothing else in the code is bounded by twelve.
