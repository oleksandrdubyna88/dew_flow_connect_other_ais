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

## Native AOT evidence (task t4, 2026-11-23)

The subset reader (`src_mcp/core/Work/StrictYamlReader.cs`) is code of this repository, so the check is the publish of the server project the way CI ships it (`.github/workflows/ci.yml`: Native AOT, warnings as errors), plus the byte comparison below.

| Check | Command | Result |
|---|---|---|
| linux-x64 publish, warnings as errors, no `NoWarn` | `dotnet publish src_mcp/src/CoaiMcp.csproj -c Release -r linux-x64 -warnaserror -m:4 --disable-build-servers -o <scratch>` (SDK 10.0.114, ILCompiler 10.0.14, `PublishAot` and the trim/AOT analyzers on) | **succeeded, 0 warnings, 0 errors** (1 m 40 s); `coai-mcp` 33 465 008 bytes. `TreatWarningsAsErrors` is on in `Directory.Build.props` and no `NoWarn` was added anywhere |
| the native binary reads the plan | `coai-mcp --validate-work v2` | exit 0, `valid: true`, counts 5 epochs, 24 epics, 22 stories, 60 tasks (every `feature.yaml`, `epoch.yaml`, `epic.yaml`, `story.yaml` and `task.yaml` of `v2/`) |
| B12: native and JIT give the same bytes | `COAI_CONTRACT_EXE=<the native binary> CoaiMcp.Tests --filter-class "*TheBinaryAnswersLikeTheInProcessValidatorTests"` | **passed**: all 91 vectors of `shared/work-plan-vectors.json` and the `v2/` folder give the same stdout bytes and the same exit code from the native process and from the JIT validator in the test process (same test without `COAI_CONTRACT_EXE`: the built JIT binary against the in-process validator, also passed) |

Not run: `win-x64`. A Native AOT publish links with the target platform's toolchain, so it cannot be made from this Linux host; the release workflow publishes it on a Windows runner. The reader uses no platform API (it is text in, a value out), so no platform difference is expected, and the release smoke runs the same test through `COAI_CONTRACT_EXE`.

The analyzers found nothing to fix in the reader or the validator: no reflection, no `Activator`, JSON written by hand with `Utf8JsonWriter`, regexes through `[GeneratedRegex]`.
