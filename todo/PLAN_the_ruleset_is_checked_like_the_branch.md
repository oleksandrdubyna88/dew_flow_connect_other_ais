# PLAN — the tag ruleset is checked like the branch is

> Status: **plan only, nothing implemented yet, 2026-09-21.** Scope: `.github/scripts/branch-protection.mjs`,
> which is byte-identical in seven repositories, plus `.github/tag-ruleset.json` in the two that have one.
>
> Related docs: [PLAN_family_ci_hardening.md](PLAN_family_ci_hardening.md) (Epic 5 step 3, which created
> the rulesets), [PLAN_release_please_meets_a_narrative_changelog.md](PLAN_release_please_meets_a_narrative_changelog.md)
> (the release path the rulesets protect).

## The symptom

Branch protection and tag rulesets are both **settings**: GitHub reads no file for either, nobody
reviews a change to one, and nothing goes red when one drifts. That argument is already written into
`.github/branch-protection.json`, and the answer to it is `branch-protection.mjs` — a reviewed file
plus a command that prints the difference between it and reality.

**Half of that answer now exists and half does not.** On 2026-09-21 Epic 5 step 3 created tag
rulesets in `dew_flow_sidecar_rust` (id 23780053) and `dew_flow_creds_for_devs` (id 23780086) and
recorded each as `.github/tag-ruleset.json` — the PUT body verbatim. The file was compared against
the live ruleset **once, by hand, in the session that created it**. Nothing will compare them again.

So today:

| | branch | release tags |
|---|---|---|
| reviewed statement of intent | `.github/branch-protection.json` | `.github/tag-ruleset.json` |
| a command that prints drift | `branch-protection.mjs` | **nothing** |
| runs in CI without a token | `--selftest`, 26 cases | **nothing** |

A ruleset edited in the web page — a bypass actor added, `enforcement` set to `disabled`, a pattern
removed — leaves the file saying one thing and GitHub doing another, silently and for as long as
nobody looks. That is precisely the state the branch tool was written to end, and the tags now
protect the release path, which is the part CWE-522 was raised about.

## The goal

`branch-protection.mjs` also reads `.github/tag-ruleset.json`, asks GitHub what the repository
actually has, and reports the difference — under the same exit codes and the same selftest
discipline it already uses for branches.

## Why WIDEN the existing tool rather than write a second one

The reuse-first rule, and in this case it is not close: every part of the plumbing exists, and a
second tool would duplicate all of it and then drift from it. Verified line references, `main` of
`dew_flow_connect_other_ais` at the time of writing (767 lines, identical in all seven repositories):

| seam | where | what it already does |
|---|---|---|
| `resolved(name)` | `branch-protection.mjs:236` | finds `gh` on PATH, absolute and runnable, refusing an implicit cwd entry |
| `ask(repo, branch)` | `branch-protection.mjs:301` | the GET, through `gh api` |
| `put(repo, branch, body)` | `branch-protection.mjs:307` | the write, body on stdin |
| `unavailable(error)` | `branch-protection.mjs:649` | the 403 a private repository without Pro gets, said in words |
| `differences(want, have)` | `branch-protection.mjs:210` | field-by-field comparison with both values |
| `current(repo, branch)` | `branch-protection.mjs:682` | the 404 split: *not protected* is drift, *not found* is exit 3 |
| `main(argv)` | `branch-protection.mjs:738` | the worst-of loop and the exit codes |
| `selftest()` | `branch-protection.mjs:615` | the harness the whole thing is held by |

**`unavailable()` already covers the tag case without a line changed.** Measured 2026-09-21:
`GET /repos/oleksandrdubyna88/dew_flow_rag_qln/rulesets` answers
`"Upgrade to GitHub Pro or make this repository public to enable this feature."` — the same sentence
`unavailable()` matches on for branch protection.

## What is NOT the same, and has to be designed rather than copied

This is the part that makes it a plan instead of an afternoon.

1. **A ruleset is one of a LIST, not a singleton.** Branch protection lives at exactly one URL per
   branch. Rulesets live at `GET /repos/{repo}/rulesets` and are addressed by a **server-assigned
   numeric id**. The id is in each file's `$note` today, but an id is not reviewable content — it
   says nothing about intent and cannot be written before the ruleset exists.

   **Filter to `target: "tag"` FIRST, then match on `name`** (`"release tags"`), and treat *two with
   that name* and *none* as two different, named failures. The id stays a note for a human
   re-applying by hand.

   The filter is not a nicety: `GET /repos/{repo}/rulesets` returns **every** ruleset, branch-target
   ones included, so a branch ruleset that happened to be called `release tags` would either be
   compared against a tag file or counted as a duplicate of one. The filter goes before all three
   checks — duplicate, no-match, and the absent-file case in question 5 — or each of them answers
   about the wrong set.

2. **The GET is not the PUT.** The answer carries `id`, `node_id`, `source`, `source_type`,
   `created_at`, `updated_at`, `_links`, and `bypass_actors` entries richer than the ones sent. It
   needs its own reducer, in the shape of `normalise` (`branch-protection.mjs:70`) — and, as there,
   **the same reducer must run over both sides** so a file's bare value and an answer's wrapped one
   compare equal. That symmetry is what the first selftest of `normalise` caught three bugs in.

3. **Creating is not updating.** Absent → `POST /repos/{repo}/rulesets`; present → `PUT
   /repos/{repo}/rulesets/{id}`. `applyTo` (`branch-protection.mjs:661`) does one PUT and knows
   nothing of this.

4. **Silence must keep meaning silence — when there is nothing to be silent about.** Five of the
   seven repositories have no `.github/tag-ruleset.json` and must stay exit 0 **provided they also
   have no tag-target ruleset**: no file and no ruleset means no opinion, not drift. The same rule
   already governs a field the branch file does not mention (`wanted`, `branch-protection.mjs:135`),
   so the shape exists; it just has to be applied at file level too.

   The proviso is the whole of the difference between this and question 5, and leaving it out is how
   the two ended up contradicting each other in this plan's first draft.

5. **A ruleset with no file is the interesting direction.** If a `tag-ruleset.json` is absent but a
   tag ruleset EXISTS, that is somebody having made settings nobody reviewed — the exact thing this
   tool is for. Reporting it is a deliberate choice and should be made on purpose, not by default.
   **Proposal: report it, at exit 1, naming the ruleset.** A repository that genuinely wants one
   writes the file.

6. **It costs a propagation round.** The tool is byte-identical in seven repositories and that is a
   family rule. This would be the fourth such round in a day; every one so far has attracted review
   findings worth having, which is an argument for doing it deliberately rather than quickly.

## Build order

1. **The reducer and the comparison, with the selftest first.** `normaliseRuleset(got)` and the
   `CASES`-style table beside the existing one (`branch-protection.mjs:316`): a matching ruleset, a
   changed `enforcement`, an added bypass actor, a removed pattern, a rule dropped from
   `creation`/`update`/`deletion`. No network, runs in CI as `--selftest` does today.
2. **Reading**: find the ruleset by name, normalise, compare, print drift under the existing exit
   codes. Verified against the two live rulesets, which are known-good and known to match their files.
3. **Writing**: `--apply` creates when absent and updates when present.
4. **The absent-file / present-ruleset case** from design question 5.
5. **Propagate byte-identically** to all seven, and check with `cmp` as every round has.

## Test plan

* `--selftest` covers every case above and stays runnable without a token, because that is what makes
  it a CI check rather than a command somebody remembers.
* Each new case is watched RED first — against the unfixed reducer — as the branch cases were, and
  the red message must name the field, not merely fail.
* Live check against `dew_flow_sidecar_rust` and `dew_flow_creds_for_devs`: **exit 0**, because their
  files were compared to reality when they were written.
* Live check against a repository with neither file nor ruleset: **exit 0**.
* Live check against `dew_flow_rag_qln`: **exit 3** with the Pro message, from `unavailable()`
  unchanged.
* A deliberate drift, made and then reverted: `enforcement` set to `disabled` on a live ruleset must
  produce exit 1 naming that field.

## Definition of Done

- [ ] `branch-protection.mjs` reads `.github/tag-ruleset.json` when present, and says nothing only when **both** the file and any tag-target ruleset are absent — an absent file beside a live one is exit 1 per design question 5, which the first draft of this line contradicted.
- [ ] Drift in a tag ruleset is exit 1 with the field named; a repository that cannot have rulesets is exit 3 with the reason.
- [ ] Rulesets are filtered to `target: "tag"` before any of the duplicate, no-match or absent-file checks run.
- [ ] `--apply` creates and updates, and is verified against a real ruleset.
- [ ] The selftest covers the reducer, and each case was seen red before it was seen green.
- [ ] The file is byte-identical in all seven repositories, checked with `cmp`.
- [ ] `.github/tag-ruleset.json` in both releasing repositories is unchanged by this work — it is already the PUT body verbatim.
