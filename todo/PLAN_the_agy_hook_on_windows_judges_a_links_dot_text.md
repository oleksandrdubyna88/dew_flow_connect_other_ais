# PLAN — the agy read-only hook on Windows judges a link's own `..` with the Unix rule, and NT reads it as text

> Status: **plan only, nothing implemented yet, 2026-11-18.** Scope: `src_mcp/runners/Reviewers/AgyHookPlaces.cs` (the guarded reader),
> `src_mcp/runners/Reviewers/HookSite.cs` (only if the flag needs a second use), their tests, and the documents that list the hook's limits
> (`SECURITY.md`, `research/RESULTS_agy_read_file.md` §7). Windows only. Linux, the Team server and WSL are not affected: there the kernel
> walks a link's text and `RealPath`'s Unix rule is the kernel's rule.
>
> Found by the Opus review of epic E1 of the parent plan,
> [PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md](../research/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md), 2026-11-18. E1 refuses a dot
> segment in the `Path` the model sends. It left "a dot segment in a link's own text" as a limit and called it "left to the kernel". That was
> wrong for Windows: this plan is the repair.

## 1. Problem

On Windows the hook can judge a read inside a granted root while NT reads a file outside it, through a relative symbolic link whose stored
text contains `..`.

**The scenario.** The granted root is `R`. Inside it:

- `R\b` is a symbolic link to `deep\er` (a folder two levels down, inside `R`).
- `R\a` is a symbolic link to `b\..\..\outside\secret.txt` (relative text).
- `R\..\outside\secret.txt`, that is a file next to `R`, is outside every root.

The model sends `Path` = `R\a`. The path itself has no dot segment, so the rule E1 added (`HasDotSegment` on `Path`) does not fire.

- **What the hook judges.** `RealPath.Resolve` reads `R\a` raw and pushes its text in front of the rest. It then walks `b`, which is the link
  `R\b`, so the resolved path becomes `R\deep\er`. The first `..` goes to `R\deep`, the second to `R`, and `outside\secret.txt` is appended:
  `R\outside\secret.txt`. That is inside `R`. The hook allows. This is the Unix rule: `..` applies to the path resolved so far
  (`RealPath.cs:324` calls `Up`, which pops the chain at `RealPath.cs:328`; the link text is pushed by `Follow` at `RealPath.cs:360`).
- **What NT reads.** Win32 and the NT path rules fold `.` and `..` in a path as text before they follow a link. Whether they do that to
  the text of a link met on the way is the open question, and the probable answer is yes: `b\..\..\outside\secret.txt` folds to
  `..\outside\secret.txt` relative to the link's folder `R`, which is the file next to `R`. NT reads outside the root.

**Where the rule stops.** `AgyHookPlaces.Admitted` (`AgyHookPlaces.cs:66`) applies the dot rule to `Path` only, and only when
`site.FoldsDotsAsText`. The link's text goes through the guarded reader (`AgyHookPlaces.Guarded`, `AgyHookPlaces.cs:22`) that asks only
whether the PATH being looked at is local. Nothing looks at the text of an `Entry.Link` it returns.

**Preconditions, which keep it narrow.**

- It needs a symbolic link, not a junction. A junction cannot hold a relative or a dotted text: its target is stored absolute.
- A symbolic link on Windows needs Developer Mode or administrator rights, or a git checkout with `core.symlinks=true` that holds such a link.
  A consult grants the person's own checkout, so the link is something the person's repository holds. A repository the person cloned from
  somebody else can hold it.
- Two links are needed: a folder link that moves the resolved path to a different depth than the text suggests, and the link with the dots.
  With no folder link in the way, the Unix rule and the text rule agree, because the resolved path IS the textual one.
- agy must then read the file: its own boundary for the path it is given is separate and was not measured for this spelling.

**Not measured.** Nothing here was run on a real machine. The Windows run of E1 (RESULTS §7) used a junction, which cannot hold this text. The
first step of the build is the measurement.

## 2. Options

**A (recommended). The guarded reader refuses a link whose text has a `.` or `..` segment, where the OS folds dots as text.**
When `Entry.Kind` is `Link` and `site.FoldsDotsAsText` is true and `HasDotSegment(entry.LinkText)`, the guarded reader throws
`IOException`, which `RealPath` turns into a failed resolve, so the call is denied. It reuses `HasDotSegment` and the flag E1 added, so the
rule for `Path` and the rule for a link's text are one rule with one flag. It is also what closes the absolute-target case ("what NT does
with an absolute link target that holds `..` is not measured"): an absolute link text with a dot segment is refused the same way.

- Cost: a link with a harmless `..` in its text (`..\shared\x`, common in a repository that links between folders) is refused, and so is a
  root reached through such a link. That is a false denial, on Windows only, and the model cannot work around it. It is the same trade E1 made
  for `Path`.
- It applies to every path the hook resolves: `Path`, roots, `cwd` and the coai folder. A root reached through a dotted link is then denied,
  which is the safe direction.

**B. Resolve the link text with the Windows rule.** Make `RealPath` fold the dots of a link's text before it pushes the text. This changes
the one shared resolver that three other sites rely on, for the sake of one OS and one caller. `RealPath`'s own remark says the shared rule
does not change. Rejected, as E1 rejected it for `Path`.

**C. Do nothing, state the limit.** The limit is already in `SECURITY.md`. Not acceptable once the scenario is a probable escape rather than
a theory, unless the measurement in step 1 shows NT does not fold link text.

## 3. Build order

1. **Measure first, on Windows with Developer Mode if one is available** (or an administrator shell). Build the scenario in section 1 in a
   scratch folder, with real symlinks. Record, in `research/RESULTS_agy_read_file.md` (a new dated paragraph under §7): (a) what
   `type R\a` (and the same read through the real `coai-mcp --agy-hook`) does; (b) what the hook says; (c) whether agy's own read opens the
   outside file. This decides whether the escape is real. If it is not, the plan states it, closes with the measurement, and the limit in
   `SECURITY.md` is rewritten. If no Windows machine with symlink rights can be had, say so, and go on with A, which is cheap and fails closed.
2. Write the tests of section 4. The row with two stand-in links fails first on every OS with the flag on.
3. Implement option A in `AgyHookPlaces.Guarded` (it needs `site.FoldsDotsAsText`, so it takes the site or the flag as a parameter). Keep
   `AgyHookPlaces.cs` free of any direct disk access: the scan in N13 (`TheAgyHookLooksOnlyAtLocalPathsTests`) must stay green.
4. Run the four `TheAgyHook*` classes, `AntigravityReadOnlyTests`, `JunctionsTests` and the server's `TheServerBinaryAnswersTheAgyHookTests`.
5. Update `SECURITY.md` (the agy hook item: the limit becomes a rule, with a dated note and the old text kept as history),
   `research/RESULTS_agy_read_file.md` §7, and `src_vs_code/CHANGELOG.md` if release-please does not write it.

## 4. Test plan

- **T-row, on every OS, through the reader seam.** Two stand-in links in an `EntryReader`: `root/b` → `deep/er` and `root/a` →
  `b/../../outside/secret.txt` (written with the separators of the OS under test). The site has `folds: true`. A control row first shows the
  same payload with a link whose text has no dot segment is allowed. Today: allow (red). After the fix: deny, with the path reason. The same row
  with `folds: false` stays allowed, which pins the flag: on Unix the kernel's rule is the hook's rule.
- **A link with a harmless `..`, same seam.** `root/c` → `../root/inside.txt`: with `folds: true` it is denied, with `folds: false` allowed.
  The test says so in its name, so a later change cannot flip it unseen.
- **An absolute link text with a dot segment.** `root/d` → `<root>/sub/../inside.txt` (absolute): denied with `folds: true`. This is the case
  the E1 limit called not measured.
- **A root reached through a dotted link** is denied with `folds: true` (the rule covers every path the hook resolves).
- **A Windows `[Fact]` with real symlinks.** It creates the two links of the scenario and runs the product's site (`HookSite.OfThisProcess()`).
  It skips, with the reason, where the account cannot create a symbolic link (`AgyHookFixture.Link` already does that). The expected
  decision is deny.
- **N13 stays green:** the place check reaches the disk only through `site.Read`.

## Definition of Done

- [ ] Step 1 measured on Windows with a real symlink, or the plan says it could not be and why, and option A goes ahead.
- [ ] A relative link whose text holds `.` or `..` is refused where the OS folds dots as text; the T-row is red before and green after, on every OS.
- [ ] The control rows (no dot segment in the link text; the flag off) stay allowed.
- [ ] The Windows `[Fact]` with real symlinks is green on a Windows machine with the privilege, or skips with its reason elsewhere.
- [ ] The four `TheAgyHook*` classes, `AntigravityReadOnlyTests`, `JunctionsTests` and `TheServerBinaryAnswersTheAgyHookTests` are green.
- [ ] `SECURITY.md` and `research/RESULTS_agy_read_file.md` §7 say what shipped, with a dated note; the old limit stays as history.
- [ ] The plan is promoted to `research/` with its deviations recorded.
