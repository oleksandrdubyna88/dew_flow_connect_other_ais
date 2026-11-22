# YAML reader for validate_work under Native AOT - results, 2026-11-23

Story s2-work-plan-schema, task t1-yaml-aot-spike (epoch 3, E1). Facts only.

## Decision

**The strict subset reader. YamlDotNet is NOT added.**

The reason is the package policy, not a measurement. `.agents/conventions/csharp/nuget-packages.md` sorts a new package by its
publisher before anything else:

| Question | Answer for YamlDotNet (checked on nuget.org, 2026-11-23) |
|---|---|
| Latest stable | 18.1.0, published 2026-06-26 (fresh) |
| Author | Antoine Aubry (one person; the repository is `aaubry/YamlDotNet`, a personal account) |
| Licence | MIT |
| Class in the policy table | "a private individual or a small personal project" - "A GitHub organisation with a single personal maintainer counts as a private individual, however popular the package is." |
| Action the policy demands | **Always ask first**, with a justification |

The task brief says: if the policy needs an approval, stop the YamlDotNet path and use the subset reader. No owner approval is
on record for this package (`Directory.Packages.props` records approvals, for example TreeSitter.DotNet 2026-09-15), so the
package was not added to `Directory.Packages.props`, and no `RepresentationModel` spike was built.

What the owner can still decide: if the owner approves YamlDotNet (with the justification "full YAML for hand-written plans"),
the spike can be run in a later story. The reader sits behind `IYamlReader`, so that change is local to one class.

## Why the subset is enough (evidence from the repository)

All 113 `*.yaml` files under `v2/` are written by `v2/_gen/gen-v2.mjs`, whose emitter writes only: block maps, block lists,
the empty list `[]`, plain scalars, JSON-quoted strings, integers and booleans, and `#` comment lines. A scan of the
files (2026-11-23) found no anchor, alias, tag, flow map, block scalar (`|` or `>`), second document or duplicate key, and no
file above 20 KiB (the reader's cap is 64 KiB). The only `[` characters in them are inside quoted titles.

## What the subset reader accepts (and everything else is an error)

Block maps, block lists (a list may sit at the same indent as its parent key), `[]`, plain scalars, double-quoted scalars
(JSON escapes), single-quoted scalars (`''`), comments, one optional leading `---`. Plain `true`/`false` is a boolean, a plain
integer is an integer, a quoted scalar is always a string, anything else plain is a string. Refused with a named error:
anchors, aliases, tags, flow collections other than `[]`, block scalars, a second document, duplicate keys in one map, tabs as
indentation, a file above 64 KiB.

## Native AOT evidence

Recorded at the end of task t4 (the reader is code of this repository, so the check is the publish of the server project):
see the section below, filled in when the publish ran.

<!-- AOT-EVIDENCE -->
