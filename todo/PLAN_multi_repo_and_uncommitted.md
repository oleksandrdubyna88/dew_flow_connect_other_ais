# PLAN — a change that spans several repositories, and a change that is not committed yet

> Status: **plan only, nothing implemented yet — and the gate's own verdict is `call_human`.**
> Three rounds: 13 findings, then 9, then 9, all 31 accepted and applied. The third round's seven
> gating findings are folded in below, but the rounds are spent, so **whether to build this is the
> operator's decision, not this document's.** §"What the gate changed" records what each round moved. Scope: `src_mcp/src/Tools.cs`,
> `src_mcp/src/Server/PanelService.cs`, `src_mcp/src/Server/SessionStore.cs`,
> `src_mcp/runners/Worktrees/WorktreeManager.cs`, `src_mcp/runners/Context/ContextAssembler.cs`,
> `src_mcp/core/Context/DiffShaper.cs`, `src_mcp/core/Gate/FindingDedup.cs`.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [module_server.md](../research/module_server.md),
> [architecture.md](../research/architecture.md).

## The two symptoms

**One.** A change that touches three repositories is reviewed three times, and never once as the
change it actually is. Each `review_code` call opens its own session over its own repo+branch
(`SessionStore.cs:180` keys a session by exactly that pair), so three sets of findings arrive with
no gate over their sum, no reviewer that ever saw both sides of a contract, and three separate counts
against a threshold that was meant to describe one change. The failure this misses is the only
interesting one in a multi-repo change: **the halves disagreeing.**

**Two.** Work that is not committed yet cannot be reviewed at all. `ReviewCodeAsync` resolves the
branch through `WorktreeManager.ResolveShaAsync` (`WorktreeManager.cs:38`), which runs
`git rev-parse --verify <branch>^{commit}` — a committed object or nothing. Everything downstream
depends on that SHA: the worktree is pinned to it (`PanelService.cs:425`) and the diff is
`git diff --numstat <baseRef>..<sha>` (`ContextAssembler.cs:53`). So the gate can only ever see work
already written into history, and the natural way to use it — check *before* committing, so a bad
commit is never made — is the one way it does not work.

Neither is a defect in what was built. Both are the single-repository, already-committed assumption
the whole context path was designed around, and both are now the common case.

---

# Part One — the working tree as a reviewable thing

## The decision: snapshot it into a real commit object, and change nothing else

The temptation is to teach the context path a second mode: "sometimes there is a SHA and sometimes
there is a dirty directory". That forks `ContextAssembler`, `WorktreeManager`, the round record, the
panel's SHA display and the reviewers' read-only guarantee — five places, for one input shape.

Instead, **make the working tree into a SHA before anything else runs**, using a temporary index. The
full command sequence, with every environment variable that has to be set:

```sh
# cwd = <repoPath> for every command; the launcher already takes a working directory
# (ProcessRequest("git", args, repoPath), ContextAssembler.cs:84), and GIT_WORK_TREE is set
# explicitly as well so the scan cannot follow the server's own cwd if that ever changes.
#
# GIT_INDEX_FILE is resolved to an ABSOLUTE, normalised path before the first subprocess:
# git interprets it relative to the process's own working directory, which is <repoPath> here,
# so a relative data-dir path would write the index INSIDE the repository being reviewed.
export GIT_INDEX_FILE=<absolute dataDir>/snapshots/<sessionId>-<round>-<repoAlias>.idx
export GIT_WORK_TREE=<repoPath>

# HEAD is PROBED, never assumed: `rev-parse --verify HEAD` exits 1 on a repository with no
# commits, so an unconditional call would fail before the unborn branch could be taken.
HEAD_SHA=$(git rev-parse --verify -q HEAD) || HEAD_SHA=""   # "" == unborn

[ -n "$HEAD_SHA" ] && git read-tree $HEAD_SHA   # skipped when unborn: the index starts empty
git add -A                    # respects .gitignore; untracked files included
git write-tree                                       -> <tree1>
git add -A ; git write-tree                          -> <tree2>   # the stability check, below

# HEAD read a SECOND time, immediately before the commit. Resolving it once is what makes the
# snapshot coherent; comparing it twice is what makes the promised refusal actually happen.
NOW=$(git rev-parse --verify -q HEAD) || NOW=""
[ "$NOW" = "$HEAD_SHA" ] || refuse "HEAD moved while the snapshot was being taken"

GIT_AUTHOR_NAME="coai" GIT_AUTHOR_EMAIL="coai@localhost" \
GIT_COMMITTER_NAME="coai" GIT_COMMITTER_EMAIL="coai@localhost" \
git commit-tree <tree1> [-p $HEAD_SHA] -m "coai: uncommitted work"   -> <sha>
git update-ref refs/coai/snapshots/<sessionId>-<round>-<repoAlias> <sha>   # keeps it alive
```

**`HEAD` is resolved once and never named again.** With HEAD at A, a developer committing B between
`read-tree` and `commit-tree` would produce a snapshot whose CONTENT is based on A and whose parent
and default base are B — a diff that omits or misattributes everything B introduced. One
`rev-parse` at the top, one SHA used for the read, the parent and the default base, closes it — and a
SECOND `rev-parse` immediately before `commit-tree` is what turns the promised refusal into something
that happens. The first draft promised it and then said HEAD is "never named again", which would have
meant nothing ever compared. If HEAD has moved the round refuses and says so: the developer committed
underneath a review of their uncommitted work, and the honest answer is to run it again.

**The snapshot is kept alive by a ref, not by luck.** An unreachable commit is exactly what
`git gc --auto` prunes, and it can fire from any git command in any process touching that repository
— including the reviewers' own — between `commit-tree` and the worktree. A commit under
`refs/coai/snapshots/…` is reachable, so it cannot be collected mid-round. The ref is deleted by the
same `SnapshotScope` that deletes the temp index, and by the same `open` sweep for a session that is
no longer running; after that the objects are unreachable again and git collects them normally.

**A snapshot belongs to a ROUND, never to a session.** A resumed session takes a NEW snapshot of
whatever is on disk then — which is the only correct behaviour, since the developer has been editing
in between and the point of the feature is to see what is there now. So a sweep deleting the ref of a
round that is no longer running can never strand a resume: nothing ever reads a previous round's
snapshot. The round record keeps the SHA for the audit trail, and a stored SHA whose objects are gone
displays as a past round rather than being re-opened.

The result is an ordinary commit object, reachable for the round and unreachable after it. From
there every existing line works unchanged: the worktree pins to it, `git diff base..sha` produces the
diff, the round records a real SHA, and the reviewers get a read-only checkout of exactly what the
developer has on disk.

**Why `GIT_INDEX_FILE` and not `git stash create`.** `stash create` is the obvious tool and it is
wrong here for one reason: it does not include untracked files. A new file is the single most
review-worthy thing in an uncommitted change, and a mode that silently omits new files would be worse
than not having the mode.

**Why this is safe, stated explicitly because it is the reviewable question.** It writes objects into
`.git/objects`, creates one ref in a namespace of its own (`refs/coai/snapshots/`), and touches
nothing else: no branch, no tag, no HEAD, no remote, and — because of `GIT_INDEX_FILE` — not the
developer's staged index. `refs/coai/` is outside `refs/heads`, `refs/tags` and `refs/remotes`, so it
appears in no branch list, is pushed by no default refspec, and is deleted at the end of the round;
after that the objects are unreachable and `git gc` collects them normally.

The one thing that must never happen is a `git add` reaching the real index; that is a single
environment variable, so it gets a test that runs the whole snapshot against a repository with a
deliberately non-empty index and asserts the index file is byte-identical afterwards. The second is a
ref that outlives its round — covered by the `open` sweep and by a test that kills a round mid-flight
and asserts `refs/coai/` is empty afterwards.

**The identity is supplied, never inherited.** `commit-tree` fails outright when `user.name` /
`user.email` are unset, which is ordinary on CI images and on a fresh machine. The four `GIT_*`
variables above are always passed, so the snapshot does not depend on the developer's git config —
and the author it records is honestly `coai`, not a person who did not make this commit.

## The four states a repository can be in, and what each does

| State | What happens |
|---|---|
| Ordinary dirty tree | Snapshot as above. |
| **Unborn HEAD** (a repository with no commits) | `read-tree HEAD` is skipped, the index starts empty, and `commit-tree` is called with **no `-p`**. The base is then the empty tree (`git hash-object -t tree /dev/null`, the well-known `4b825dc…`), so the diff is "everything is new" — which is exactly true. |
| **Clean tree** | Refused before any reviewer launches, with a sentence. `write-tree` would yield HEAD's own tree and an empty diff; six launches to report nothing is worse than being told there is nothing to review. |
| **Unmerged / conflicted index** | Refused with a sentence naming the repository and the conflicted paths. `git add -A` against a temporary index fails on unmerged entries, and the useful answer is "finish the merge", not a git error. Detected with `git diff --name-only --diff-filter=U` **before** any resource is allocated. |

## The tree must not move while it is being read

`git add -A` is a traversal, not an atomic snapshot: a save landing mid-traversal produces a tree that
mixes two moments, and the DoD promises "exactly the work on disk".

**The check is the TREE, not `git status`.** The first draft compared porcelain status text before
and after, and that cannot detect the most likely case: an already-modified file edited again mid
traversal still reads ` M path` both times, so a tree containing a half-written file would pass. Both
vendors raised it independently.

So: run `add -A; write-tree` **twice** and compare the two tree object ids. A tree id is the content,
hashed — nothing changes it but a real change to a file that git can see. Equal ids mean the second
traversal saw exactly what the first did, and the first tree is used. Unequal means something moved:
retry the whole snapshot **once**, and on a second disagreement refuse, naming the paths that differ
between the two trees (`git diff-tree -r --name-only <tree1> <tree2>` — which is why the check names
files rather than saying "something changed"). Retrying forever against a running build would be a
hang, and reviewing a tree that never existed is worse than a refusal that says why.

**The limit, and the guarantee weakened to match it.** Two identical trees prove the tree was stable
across two traversals, not during the first one: a file changed after its entry was read and restored
before the second pass reached it leaves both ids equal. The first draft still listed "a moving tree
is never reviewed" in its Definition of Done, which the check cannot deliver — the reviewer was right
that acknowledging a race and then guaranteeing its absence is having it both ways.

So the promise is **best-effort detection, stated as such**: the common cases (a save during the
traversal, a build writing output, a branch switch) change the tree id and are caught; the
write-then-revert window between two passes is not, and is accepted rather than papered over. A real
guarantee needs a cross-process working-tree lock, git has none, and inventing one would mean blocking
the developer's own editor for the duration of a review. The DoD says "best-effort, detected by tree
id" and the residual race is documented in `module_runners.md` next to the mechanism.

## Every resource is owned, and abandoned ones are swept

Four kinds of resource are now created per repository per round: a temporary index file, a keep-alive
ref, objects, and a worktree lease. The objects look after themselves once the ref is gone; the other
three leak on a crash and are therefore owned and swept.

- **Owned:** one `SnapshotScope : IAsyncDisposable` per repository holds its temp index path and its
  lease, and the round holds the set. A failure on repository two disposes repository one's scope on
  the way out, in a `finally`, exactly as the single-lease `await using` at `PanelService.cs:425`
  does today.
- **Swept:** temporary index files live in **one directory under the data dir**, not the system temp,
  named `snapshots/<sessionId>-<round>-<repoAlias>.idx` — the same three parts as the keep-alive ref,
  so one sweep pass recognises both. `open` already prunes worktrees a killed session left behind; the
  same sweep deletes snapshot indexes AND `refs/coai/snapshots/` entries whose session is not running. A file in the system temp folder is somebody else's problem forever; a file under the data
  dir is this product's, and it is the only place a sweep can safely delete from.
- **When cleanup itself fails** (a locked file on Windows is the realistic case) it is logged at
  warning with the path and the round continues. A review must not fail because a temp file could not
  be deleted; the sweep will get it next time.

---

# Part Two — several repositories, one round

## The argument shape

One array; each entry is a path, optionally with refs — the two cases named in the request:

```jsonc
"repos": [
  { "path": "d:/rsd/dew_flow_mcp" },                                     // working tree vs HEAD
  { "path": "d:/rsd/dew_flow_rag_qln", "base": "origin/main" },          // working tree vs a base
  { "path": "d:/rsd/ClaudeRag", "head": "9ffa131", "base": "9ffa131^" }  // one commit
]
```

- `head` absent → the working-tree snapshot from Part One.
- `base` absent → `HEAD` when `head` is absent; `<head>^` when `head` is given — **except on an unborn
  HEAD, where both defaults resolve to the empty tree** (`4b825dc642cb6eb9a060e54bf8d69288fbee4904`).
  The four-state table already says the unborn case diffs against the empty tree; the default was
  still written as the literal string `HEAD`, which would have reached `git diff HEAD..<sha>` and died
  with `fatal: bad revision`. The default is a resolved SHA, never a ref name.
- The existing `repoPath` / `branch` / `baseRef` arguments stay and stay first-class. They are the
  single-repo call, they are what every pasted CLAUDE.md snippet in the field already sends, and a
  tool whose old form stops working breaks every one of them.

**Every reply states, per repository, what was actually compared** — `path`, resolved base SHA,
resolved head SHA, and whether the head was a snapshot. This is the answer to "omitting `base` on a
secondary repo silently turns a branch review into a working-tree review": it cannot be silent if the
reply says `dew_flow_rag_qln: 3f2a1c8 (origin/main) → snapshot of working tree`. A default that is
wrong for a caller is then visible in the same message that would have hidden it.

## The seven things that actually have to change

### 1. Session identity, and reaching it from a secondary repository

`SessionKey.For(repoPath, branch)` (`SessionStore.cs:180`) keys the session by the **primary** repo —
the first `repos` entry, or the `repoPath` argument. Additional repositories are session *state*, not
session *identity*, so every persisted session on disk stays readable and `resolve` keeps the
signature every caller already uses.

But an agent working in repository two will call `status` with repository two's path, and "no session
for this repo+branch" would be a lie. So the store also writes an **alias record** per participating
repo+branch pointing at the primary key. Lookup: exact key first, then alias. An alias whose primary
session no longer exists is deleted on read rather than returned.

### 2. Persistence, resume, and a repository that moved

The session record gains `repos: [{path, base, head, wasSnapshot, resolvedBaseSha, resolvedHeadSha}]`,
written with the round. Two rules, both because a session outlives the machine state it described:

- **The refs are re-resolved every round, the paths are not.** A branch moves between rounds and the
  next round must see the new code.
- **A path is not an identity, so the identity is stored beside it.** A repository can be deleted and
  a different one cloned to the same path between rounds; re-resolving refs there would review other
  code under this session's alias and produce a confident verdict about the wrong thing. So the record
  keeps, per repo, the absolute `git rev-parse --git-common-dir` and the SHA of its **root commit**
  (`git rev-list --max-parents=0 HEAD | tail -1`) — cheap, stable across branches and rebases of
  everything above it, and different for two unrelated repositories. A mismatch fails the round with a
  sentence naming the repository, rather than being re-resolved silently. An unborn repository has no
  root commit and is identified by the common-dir alone, with that stated in the record so a later
  round does not read the absence as a mismatch.
- **A repository that has moved, vanished, or is no longer a git repository fails the round with a
  sentence naming it** — never a partial review over the survivors, which would produce a verdict over
  two thirds of a change while looking complete. `status` reports the same thing without failing, so a
  resumed conversation can see what is wrong before it spends a round.

Schema migration: sessions written before this change have no `repos` array; reading one synthesises a
single entry from its existing `repoPath`/`branch`/`baseRef`. That is a test, not a hope.

### 3. Qualified paths, and getting back from one to a file

`FindingDedup.SameDefect` (`FindingDedup.cs:53`) matches on the normalised `File` string plus a line
slack. Two repositories both containing `src/index.ts` would have unrelated findings merged — silently,
and in the direction that **hides** a finding.

So there is exactly one canonical form, and it is defined here rather than left to each call site:

```
<repoAlias>/<path-relative-to-that-repo-root>      e.g.  dew_flow_mcp/src/Server/PanelService.cs
```

`repoAlias` starts as the repository directory's own name and is then made unique **across the whole
final set**, not just among repositories that share a basename. Disambiguation is derived **from the
path, never from the position**: `<name>-<first 6 hex of sha256 of the absolute repo path>`.

Two rules make that actually unique, and the second exists because the first is not enough:

1. Any name appearing more than once takes the suffix.
2. **The suffixed names are then checked against the whole set again**, and the digest lengthens until
   every alias is distinct. A repository called `foo` sitting beside one whose folder is genuinely
   named `foo-a1b2c3` collides on the first rule alone — and two repositories resolving to one
   subdirectory means findings pointing into the wrong tree. Names are also normalised for the target
   filesystem, since the alias becomes a directory name.

Positional suffixes (`foo`, `foo-2`) were the first draft and are wrong: reordering the `repos` array
between rounds would swap the two aliases, invalidating every stored finding's path and every
cross-round dedup. The alias is STORED per repo in the session, so a repository that moves keeps the
alias its findings already carry.

**Dequalification happens at exactly one boundary**, a single `QualifiedPath.Split(file, aliases)`
returning `(repoAlias, relativePath)` — the alias set is an argument for the reason below — used by:

| Boundary | Why it must dequalify |
|---|---|
| The worktree file read (context, and any follow-up) | the tree is rooted AT the repo, so a qualified path would look for `repo-a/repo-a/src/…` |
| The panel's "open this finding" navigation | it opens a real file on disk |
| `resolve` persistence | a finding must still resolve after a restart |
| The rounds view display | a person needs to see which repository |

**`Split` is session-aware, never a guess at the first path segment.** Single-repo rounds keep
unqualified paths — nothing about the existing shape changes and every stored finding stays valid —
which means `Split` is handed a mixture. Given `src/index.ts` it would happily report the repository
`src`, and then look for `index.ts` at the wrong root. So the signature takes the round's alias set:
`QualifiedPath.Split(file, aliases)` splits only when the first segment IS one of the aliases the
session recorded, and otherwise returns `(primaryAlias, file)` unchanged. A single-repo round passes
an empty alias set and every path stays whole by construction.

### 4. The diff budget: a reserved floor, then a deterministic redistribution

`DiffShaper.DefaultMaxBytes` is 192 KB per round (`DiffShaper.cs:28`), spent in file order
(`DiffShaper.cs:34-52`). Concatenating three repositories lets the first eat the budget and the third
arrive as "NOT shown" — a reviewer silently reviewing one third of a change while the round looks
complete.

An even split is not enough either: when every repository exceeds its share, an unconstrained second
pass can still spend everything in file order and starve the last one. The invariant, stated so it can
be tested:

> **Every repository with at least one changed file contributes at least its largest whole file that
> fits in `floor = maxBytes / (4 × repoCount)`, or — if not even that fits — its numstat summary and
> an explicit "not shown" list.** No repository is ever represented by nothing at all.

The allocation is then: reserve the floor for each repo; spend each repo's own share in its own file
order; collect the unspent remainder; redistribute it in **one** pass, round-robin over repositories
in their declared order, one file at a time — so a large repo cannot take the whole remainder before a
small one is offered any of it.

### 5. The reviewer contract: one root, and it is the parent

Concatenating diffs does not make the launch path multi-root, and `IReviewerRuntime.Build` takes ONE
working directory. The contract is settled here rather than left to the trace:

> **Every runtime is launched with the ROUND ROOT as its single working directory** — a directory
> this product creates, containing one worktree per repository, each checked out AT
> `<roundRoot>/<repoAlias>`. No runtime gains a second root and no interface grows a map, because a
> sandbox told about several roots is a sandbox each vendor implements differently — `-s read-only
> --ephemeral`, `--approval-mode plan` and the local shim each read their root from a different place.

**The round root is BUILT, not found, and the first draft got this wrong.** It said "the parent
directory", as though the repositories had one: `C:\work\a` and `D:\src\b` share no parent at all,
and even two repositories under one folder would put the wrong siblings in the reviewer's root.
Nothing forces a worktree to live next to its repository — `git worktree add <path> <sha>` takes any
path, and the leases already live under the data dir today (`PanelService.cs:425`). So the lease
target becomes `<dataDir>/rounds/<sessionId>-<round>/<repoAlias>`, and the round root is its parent:
one real directory, on one filesystem, containing exactly the aliases the findings use — with no
symlinks or junctions, which would need elevation on Windows and are followed inconsistently by
sandboxes.

A qualified path `dew_flow_mcp/src/…` then resolves from the reviewer's own cwd with no translation,
which is the reason to prefer this over a root map. The prompt's context block names the repositories
and says a path is qualified by the directory it is in.

**The risk this creates, and why it fails CLOSED.** A root that is not itself a git repository is a
shape the vendors have never been given: a sandbox may refuse a path outside the repository it
detects, or detect nothing at the root. So step 6 begins with a **one-round smoke against every
enabled vendor** — two real repositories, a question whose answer is only in the second.

A vendor that cannot read across the root is **not** quietly marked unsupported and skipped. That was
the first draft, and it is the worst available option: the round would produce a normal merged verdict
while missing exactly the cross-repository findings only that vendor might have raised, and nothing in
the reply would say the review was partial. Instead:

> **A multi-repo round refuses to run while any ENABLED provider cannot read across the round root**,
> naming the vendor and saying that disabling it, or reviewing the repositories separately, are the
> two ways forward.

The capability is probed once per vendor version and cached, so the refusal is instant rather than a
launch every round. This is the same principle the whole product runs on: a gate that cannot see
everything says so instead of grading what it happened to see.

Before any of this is written, every interface that assumes one root is enumerated with its line:
`ContextAssembler.Assemble`, `DiffShaper.Shape`, `WorktreeManager.AddAsync`,
`PanelService.BuildWork`'s `worktreePath`, each `IReviewerRuntime.Build`'s working directory, and the
`ComposePrompt` context block. Anything not on that list at implementation time means the trace was
wrong and the step stops rather than proceeding.

**The test that proves the feature exists** is not "three repos produced findings": it is a
cross-repository contract test — a caller in repo A and a signature in repo B, changed apart — where a
single-repo round finds nothing and the multi-repo round finds the mismatch.

### 6. Worktree leases, as a set

`PanelService.cs:425` takes one lease in an `await using`. It becomes a `SnapshotScope` set disposed
together, with the failure case tested directly: three repositories, the third throwing, all leases
and all temp indexes gone afterwards.

### 7. The panel

A round over several repositories has to say so; the rounds view shows one repository per row today.
Minimum: the round names its repositories, and a finding shows its `repoAlias`.

## What deliberately does NOT change

- **The gate — and here is the data flow, because "it does not change" is a claim, not evidence.**
  A round is one `BuildWork` call producing one list of reviewer invocations, whose answers are merged
  once by `FindingDedup.Merge` and evaluated once by `GateRule.Evaluate(merged, rejections, threshold)`
  (`PanelService.cs:~468`). Nothing in that path is per-repository; repositories enter as CONTEXT
  — more worktrees, a bigger diff — not as more rounds. So three repositories produce ONE merged
  list and ONE verdict for the same reason two vendors do today, and the three-independent-thresholds
  failure the reviewer describes is a thing that would have to be ADDED. What is added is the test:
  findings distributed across three repositories, one threshold evaluation, one verdict — plus a
  resume of that round asserting no finding is counted twice.
- **The scope.** `CodeScope.Floor` (200 characters) applies to the change as a whole: one change
  spanning three repositories has one intent, and asking for three would be asking the caller to
  invent differences that do not exist.
- **The plan stage.** A plan is text and has no repository. `review_plan` is untouched.

---

## What the gate changed

The gate returned 13 findings over this plan; all were accepted. Seven of them changed a decision
rather than adding a paragraph, and they are worth naming because each was a way the first draft would
have shipped something broken:

| Finding | What it moved |
|---|---|
| Unborn HEAD / unset git identity (raised independently by **both** vendors) | the four `GIT_*` variables and the no-`-p` case — the first draft's command sequence simply failed on a fresh repo or a CI image |
| `GIT_WORK_TREE` / working directory | made explicit rather than relying on the launcher's cwd |
| Concurrent edits during the traversal | the before/after `status` comparison, one retry, then refusal |
| Qualified paths never mapped back | one canonical form and a single dequalification boundary, with the four call sites named |
| The budget's second pass could still starve | a reserved floor per repository and a round-robin redistribution — a stated invariant instead of a hope |
| Secondary repos could not find the session | the alias record |
| `base` defaulting silently | the per-repository "what was actually compared" line in every reply |

Round 2 returned nine over the revision; all accepted, and six of them changed a decision:

| Finding | What it moved |
|---|---|
| The snapshot commit can be garbage-collected mid-round | the keep-alive ref under `refs/coai/snapshots/` |
| `HEAD` was named three times and could move between them | resolved once, and a refusal if it moves |
| Status text cannot prove the tree held still (raised by **both** vendors) | two `write-tree` passes compared by tree id, and the residual limit stated |
| `GIT_INDEX_FILE` relative to the process cwd lands INSIDE the repository | absolute, normalised, before the first subprocess |
| `Split` would read `src/index.ts` as the repository `src` | the alias set is an argument; only a known alias splits |
| Positional `-2` aliases swap when the array is reordered | the alias is derived from the path |

Two more were answered rather than changed: the gate's data flow is now written down with its line
reference, and the multi-root reviewer contract was settled — wrongly, as round 3 then showed.

Round 3 returned nine more and the rounds ran out: **the verdict is `call_human`**, so the decision to
build this belongs to the operator. All nine were accepted and applied anyway, because a plan is text
and improving it costs nothing; what the verdict withholds is permission to implement, not permission
to think. Seven changed a decision:

| Finding | What it moved |
|---|---|
| No physical parent exists for `C:\work\a` and `D:\src\b` | the round root is BUILT under the data dir and the worktrees are checked out INTO it — the previous answer, "the parent directory", was simply false for repositories that do not share one |
| `rev-parse --verify HEAD` exits 1 before the unborn branch is reached | HEAD is probed, not assumed, and the unborn case is the empty string |
| The default base `HEAD` reaches `git diff HEAD..<sha>` on an unborn repository | defaults resolve to a SHA, never a ref name; unborn resolves to the empty tree |
| HEAD movement was promised and never checked | a second `rev-parse` immediately before `commit-tree` |
| A repository replaced at the same path is reviewed under the old session | identity is the git common-dir plus the root commit, and a mismatch fails the round |
| `foo` beside a folder genuinely named `foo-a1b2c3` collides | uniqueness is checked over the whole final alias set, with the digest lengthened until it holds |
| A vendor that cannot read the round root was to be "recorded as unsupported" | the round REFUSES while an enabled provider cannot read it — a partial review that looks complete is the failure this product exists to prevent |

Two were about promises rather than mechanisms, and both were right: a snapshot belongs to a round and
never to a resume (so no sweep can strand one), and "a moving tree is never reviewed" was an
overclaim that the tree-id check cannot deliver — it is now best-effort, with the residual race named.

## The state this plan is in

Three rounds, 31 findings, all accepted, all applied — and still seven gating at the end. That is not
a stalemate: the count fell 13 → 9 → 9 while the findings moved from "this will not run" to "this
promise is stronger than its mechanism", which is the direction a plan is supposed to travel. But the
rounds are spent and the gate's own rule is that a person decides.

**What the operator is deciding.** Not whether the design is sound — both symptoms are real and the
approach survived three rounds of attack. What is open is whether the SIZE is acceptable: Part One is
a contained change with an unusually good test story, and Part Two touches the session record, the
dedup key, the diff budget, the worktree layout and every vendor's launch root. Splitting them is the
obvious move, and the build order is already written so that steps 1–2 ship Part One alone.

## Build order

1. **Part One, alone, behind its own tests.** `WorkingTreeSnapshot` as a unit with a fake launcher,
   then against real temporary repositories for every row of the four-state table.
2. **`head` absent on the existing single-repo call.** Part One reaches the tool with no new argument
   shape — the smallest change that delivers half the value, and it is the half used daily.
3. **Qualified paths and the dedup test**, before any multi-repo plumbing, so the collision cannot
   ship even briefly.
4. **The budget floor and redistribution**, also before the plumbing, for the same reason.
5. **The session record, the alias, and the migration.**
6. **The interface trace (§5), the per-vendor parent-root smoke, then the lease set and the `repos`
   argument.** The smoke comes first inside this step: if a vendor cannot read across a parent root,
   the argument shape is the wrong thing to have built.
7. **The panel.**

## Test plan

Every item is a test that must be watched fail first.

| What | Kind |
|---|---|
| Untracked file appears in the snapshot's diff | integration, real git |
| `.gitignore`d file does not | integration, real git |
| The developer's staged index is byte-identical after a snapshot | integration, real git |
| A repository with no commits snapshots against the empty tree | integration, real git |
| A repository with no `user.email` still snapshots | integration, real git |
| A clean tree is refused with a sentence, not reviewed | unit |
| A conflicted index is refused, naming the repository, before any resource is allocated | integration |
| A file changed during the traversal causes one retry, then a refusal naming it | integration |
| An already-modified file edited again mid-traversal is caught — the case status text misses | integration, real git |
| A commit landing between the read and the commit-tree refuses the round | integration, real git |
| The snapshot survives a `git gc --prune=now` before the worktree is created | integration, real git |
| A relative data dir still writes the index outside the repository | unit |
| A repository with no commits does not fail on the initial HEAD probe | integration, real git |
| An unborn repository's default base is the empty tree, and `git diff` runs | integration, real git |
| A commit landing between the two HEAD reads refuses, naming the movement | integration, real git |
| A repository replaced at the same path fails the next round | integration, real git |
| `foo` and a folder named `foo-<digest>` get distinct aliases | unit |
| The round root contains exactly one directory per alias, on one filesystem | integration |
| Two repositories on different drives produce one round root | integration, Windows |
| A multi-repo round refuses while an enabled vendor cannot read the round root | integration |
| `Split("src/index.ts")` with an empty alias set returns the whole path | unit |
| Reordering the `repos` array does not change any alias | unit |
| Findings spread over three repositories produce ONE threshold evaluation | unit, `GateRule` |
| Every enabled vendor can read a file from the second repository under a parent root | integration, per vendor |
| `base` defaults to HEAD when the head is a snapshot, and to `head^` when it is not | unit |
| Every reply names, per repository, the base and head actually compared | unit |
| Two repos sharing `src/index.ts` produce two findings, not one | unit, `FindingDedup` |
| A qualified path resolves to a real file under its own worktree | integration |
| Every repository with a changed file is represented when all three exceed their share | unit, `DiffShaper` |
| Three repos, the third throwing: no lease and no temp index survives | integration |
| A cross-repository contract mismatch is found by a multi-repo round and missed by a single-repo one | integration, the feature's proof |
| A session written before this change still loads | unit, `SessionStore` |
| `status` from a secondary repository finds the session | unit, `SessionStore` |
| A vanished secondary repository fails the round with a sentence naming it | integration |
| The old four-argument call behaves exactly as before | regression |

## Definition of Done

- [ ] `review_code` reviews uncommitted work, including untracked files, without touching the
      developer's index, refs or working tree — proven by the byte-identical-index test.
- [ ] Every row of the four-state table behaves as written, each with a test.
- [ ] A tree that moves during the snapshot is retried once and then refused — best-effort detection
      by tree id, with the write-then-revert race named in the docs rather than claimed as covered.
- [ ] The snapshot cannot be garbage-collected before the round ends, and its ref does not outlive it.
- [ ] `HEAD` is read once, and a commit landing underneath the snapshot refuses rather than mixes.
- [ ] No temporary index or worktree survives a failed or killed round; the sweep is on `open`.
- [ ] One round can span several repositories and produces one verdict over their sum.
- [ ] A finding names its repository, two repositories sharing a path cannot merge, and a qualified
      path opens the right file.
- [ ] No repository with a changed file can be represented by nothing.
- [ ] A session is reachable from every participating repository, and one written before this change
      still loads.
- [ ] Every reply says, per repository, what was compared.
- [ ] A multi-repo round refuses while any enabled vendor cannot read the round root, and says which.
- [ ] The round root is one real directory this product created; no symlink, junction or shared parent
      is assumed to exist.
- [ ] A repository replaced at the same path between rounds fails the round instead of being reviewed.
- [ ] Aliases are unique across the whole set and identical under any ordering of `repos`.
- [ ] The existing single-repo, committed call is unchanged in behaviour and in arguments.
- [ ] `research/module_runners.md` records the snapshot mechanism and why `git stash create` was
      rejected; `module_server.md` records the session key, the alias and the migration.
- [ ] This plan reached `proceed`, and the implementation went through `review_code`.
