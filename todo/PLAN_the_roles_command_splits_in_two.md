# PLAN — the roles page's messages and the row editor's commands stop being one type

> Status: **plan only, nothing implemented yet, 2026-09-18.** Scope:
> `src_vs_code/src/rolesPage.ts`, `src_vs_code/src/rolesEdit.ts`, `src_vs_code/src/rolesPanel.ts`,
> and the same shape in `src_vs_code/src/phrasesEdit.ts` / `phrasesPanel.ts` if it is taken there too.
>
> **Extracted from [PLAN_a_deleted_role_stays_deleted.md](../research/PLAN_a_deleted_role_stays_deleted.md)
> on 2026-09-18**, where it was the one finding of that code round that was REJECTED — and rejected
> on scope rather than on merit. The reviewer was right; the change did not belong inside a story
> that was also changing what a deletion means, and the gate's own command for that task forbade
> starting a second round of splitting inside it.
>
> Related: [module_extension.md](../research/module_extension.md).

## The symptom

`RolesCommand` ([rolesPage.ts:116-131](../src_vs_code/src/rolesPage.ts)) is one union holding two
different kinds of thing:

- **transport** — what the webview POSTS: `zoom`, `tab`, `ignore`, `reloadWindow`, `finishDeletion`.
- **domain** — what edits a ROW: `add`, `remove`, `edit`, `addPrompt`, `removePrompt`, `editPrompt`,
  `restorePrompt`.

`rowsAfter` takes the whole union, so the pure row reducer has to say something about every message
the page can send — including the ones it has nothing to do with. Today that is an early return
naming **six** kinds:

```ts
  if (command.kind === 'ignore' || command.kind === 'zoom' || command.kind === 'restorePrompt'
      || command.kind === 'tab' || command.kind === 'finishDeletion' || command.kind === 'reloadWindow') {
    return UNCHANGED;
  }
```

and **two** copies of an `Exclude<RolesCommand, { kind: … }>` with the same list, at
[rolesEdit.ts:127](../src_vs_code/src/rolesEdit.ts) and
[rolesEdit.ts:151](../src_vs_code/src/rolesEdit.ts).

**The cost is felt when a button is added.** The deletion story added two controls that touch no row
at all — *Finish the deletion anyway* and *Reload Window*, both handled in `rolesPanel.apply` before
anything reaches `rowsAfter` — and paid for them in three edits to a pure module that has no
business knowing either exists. Nothing was wrong at runtime; the type system simply made the page's
vocabulary the reducer's problem.

## Why it was rejected once, and why that is not an argument against it

The finding arrived in the deletion story's code round, which was also changing the ORDER a deletion
happens in, what a tombstone is, and what the mirror tells whom. Splitting a union used by two
panels in the middle of that would have made a diff nobody could review as one thing, and the gate's
operator command for that task said in as many words: build it as one unit, do not start a second
round of splitting.

So it is here, whole, where it is the only thing in the diff.

## What this builds

**1. Two unions.** `RowEditCommand` for the seven that change a row, and `RolesCommand` as the page's
own vocabulary — the union of `RowEditCommand` and the transport kinds. `roleEdit(message)` still
returns `RolesCommand`, because that is what arrives from a webview.

**2. `rowsAfter(current, command: RowEditCommand, reserved)`.** No early return, no `Exclude`, and no
mention anywhere in `rolesEdit.ts` of a control the page happens to draw. Adding a button becomes one
edit to `rolesPanel.apply`.

**3. `rolesPanel.apply` narrows once, and the narrowing is exhaustive.** A `switch` over the
transport kinds that returns, leaving a value TypeScript knows is a `RowEditCommand`. The exhaustive
check is the point: a new transport kind that nobody handles must be a compile error, not a message
that silently falls through to the row reducer.

**4. The same shape in `phrasesEdit.ts`, or a written reason why not.** It has the identical
arrangement beside this one. Doing one and not the other leaves the next person to guess which is
the pattern.

## Build order

1. `RowEditCommand` extracted, `RolesCommand` defined in terms of it. No behaviour, no call-site
   change — the union is the same set.
2. `rolesPanel.apply` gains the exhaustive transport switch.
3. `rowsAfter` and `onRow` narrow to `RowEditCommand`; the early return and both `Exclude`s go.
4. `phrasesEdit.ts`, or the reason.

Each step compiles and the suite is green at each step, which is what makes this reviewable.

## Test plan

```bash
cd src_vs_code
npm test
```

| # | Test | Why it has teeth |
|---|---|---|
| 1 | Every existing `rolesEdit` and `rolesPage` test passes untouched. | This changes no behaviour. A test that had to be edited is a behaviour change nobody asked for, and the diff should show none. |
| 2 | A transport kind added to `RolesCommand` and handled nowhere is a COMPILE error. | The whole value of the split. Asserted by the type-level test the repository already uses for exhaustiveness, or by a `satisfies never` in the switch's default. |
| 3 | `rolesEdit.ts` mentions no transport kind at all. | Structural, pinned on the declarations rather than the prose — a source-read test reads the comments too, which this repository has been caught by. |
| 4 | Each assertion watched failing first. | `testing.md`. |

## Growth surface

**None.** Two names where there was one; no new file unless `RowEditCommand` reads better in
`roles.ts`, which is a judgement to make while doing it.

## Definition of Done

- [ ] `rowsAfter` and `onRow` take `RowEditCommand`; no `Exclude`, no early return for transport.
- [ ] A transport kind nobody handles is a compile error.
- [ ] `rolesEdit.ts` names no control the page draws.
- [ ] `phrasesEdit.ts` follows, or the summary says why it does not.
- [ ] No test needed editing; if one did, the summary says which and why.
- [ ] `research/module_extension.md` records the two vocabularies.
- [ ] Promotion per `common/planning-docs.md`, then `plan-lifecycle.mjs`.
- [ ] Through `review_plan` and `review_code`.

## What this will NOT do

It will not change what any control does, and it must not: a diff that both moves a type and alters
a behaviour is one nobody can check. If something looks wrong while doing this, it is a finding to
write down, not to fix here.
