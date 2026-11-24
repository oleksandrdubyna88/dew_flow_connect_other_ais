# The work method - the rules `validate_work` checks

This file is the readable half of the work-plan contract. The machine-readable halves are `shared/work-plan.schema.json` (keys,
types, ids, enums) and `shared/work-plan-vectors.json` (one failing and one passing example per error code). The checker is
`coai-mcp --validate-work <feature-dir>` and the MCP tool `validate_work(repoPath, featureFolder)`. It reads the working tree and
changes nothing.

## The folder tree

```
<feature>/feature.yaml
<feature>/epoch-N/epoch.yaml
<feature>/epoch-N/eN-<name>/epic.yaml
<feature>/epoch-N/eN-<name>/stories/sN-<name>/story.yaml
<feature>/epoch-N/eN-<name>/stories/sN-<name>/tasks/tN-<name>/task.yaml
```

A folder that matches none of these names (`architecture`, `spikes`, `_gen`) is not part of the plan and is not read. A symbolic link is not followed.

## Limits come from the device

The numbers are never typed into the checker. They are the `workMethod` of the `roles` answer: the shipped defaults of
`shared/role-defaults.json`, with a person's `COAI_ROLES_WORK_METHOD` over them (environment, then the settings file). The answer
of `validate_work` names them and says `source: device` (a person set one) or `source: default`, with the `settingsRevision`.
A plan that is valid on one device can be invalid on another. A `workMethod` in `feature.yaml` is a note only, never a limit.

| Limit | Default | Meaning |
|---|---|---|
| `maxEpicsPerEpoch` | 5 | epics in one `epoch.yaml` |
| `mvpEpics.min` / `.max` | 1 / 2 | the first epics (in the order of their folder numbers, `e1`, `e2`, ...) are `mvp: true`; their count is within min..max; no `mvp: true` after the first epic without it |
| `maxStoriesPerEpic` | 8 | story folders of one epic |
| `maxTasksPerStory` | 6 | task folders of one story |

An epic may have no stories yet (they are planned later). A story must have at least one task.

## Rules and error codes

| Code | The rule |
|---|---|
| `yaml-too-large` | a file above 64 KiB |
| `yaml-syntax` | the file is not YAML of the subset below |
| `yaml-forbidden` | an anchor, alias, tag, flow collection other than `[]`, block scalar (`\|`, `>`), directive or a second document |
| `yaml-duplicate-key` | a key twice in one map |
| `missing-file` | an epoch, epic, story or task folder without its YAML file; no `feature.yaml` is not a plan at all (exit 65) |
| `wrong-type` | a value of the wrong type: `complexity: "5"` (a string), `parallel: yes` (a plain `yes` is a string), a list where a map belongs |
| `missing-key` | a required key is absent |
| `unknown-key` | a key below the feature level that the schema does not name (`secuirty`) |
| `bad-id` | an id does not match its pattern in full: epoch `^epoch-\d+$`, epic `^e\d+-[a-z0-9-]+$`, story `^s\d+-[a-z0-9-]+$`, task `^t\d+-[a-z0-9-]+$`, dod `^d\d+$` |
| `bad-value` | an enum or range fails: `kind`, `status`, `complexity` outside 0..10, a `files` entry that is not a path inside the repository |
| `folder-id-mismatch` | the folder name is not the id of the file in it (epoch, epic, story, task). The feature id is free |
| `list-folder-mismatch` | the `epochs` list of `feature.yaml` or the `epics` list of an `epoch.yaml` is not the set of folders |
| `too-many-epics` | more epics than `maxEpicsPerEpoch` |
| `too-many-stories` | more stories than `maxStoriesPerEpic` |
| `too-many-tasks` | more tasks than `maxTasksPerStory` |
| `mvp-count` | the number of leading `mvp: true` epics is outside `mvpEpics.min..max` (an epoch with fewer epics than `min` needs all of them) |
| `mvp-order` | an epic is `mvp: true` after an epic that is not |
| `story-without-tasks` | a story folder with no task |
| `unknown-dependency` | a `dependsOn` entry names nothing (see below) |
| `ambiguous-dependency` | a `dependsOn` entry names two things |
| `dependency-cycle` | the dependencies go round; the message holds the path |
| `parallel-dependent` | two `parallel: true` sibling tasks where one depends on the other, directly or through other siblings |
| `parallel-files` | a `parallel: true` task with no `files`, or with a glob (`*`, `?`, `[`, `{`) in a `files` entry |
| `parallel-overlap` | two `parallel: true` sibling tasks whose `files` overlap: equal, or one is a path prefix of the other (case-insensitive) |
| `empty-dod` | a task with no `dod` item |
| `duplicate-dod-id` | two `dod` items of one task with the same id |
| `security-contradiction` | `kind: security` with `security: false` |

## Dependencies (`dependsOn`)

An entry resolves to a node of the same level, never to another level:

1. **A sibling**: an epoch of the feature, an epic of the same epoch, a story of the same epic, a task of the same story. A node that depends on itself is a cycle.
2. **The same level in an epic (or epoch) this one depends on** (decision 0.4 of the epoch 3 E1 spec): a story or task id is looked up in the epics that the node's own epic lists in its `dependsOn`; an epic id is looked up in the epochs that the node's epoch lists. One match resolves it, two are `ambiguous-dependency`.
3. **`<epic>/<story>`** for a story: the story named in the epic named (any epic of the feature).

Anything else is `unknown-dependency`. Cycles are searched over all resolved edges of one level.

## Notes (never errors)

- `feature.yaml` holds a `workMethod` number that differs from this device's limit.
- A task text (title, a dod item) holds a word of `shared/credential-words.json` while the task has no `security: true`.

## `important`

The answer lists every task that must run on the `important` model list, with the reason: `security: true`, `kind: security`, or
`complexity: 10`. The router uses the same rule (`ImportantRule`).

## The YAML subset

Block maps, block lists (a list may sit at the same indent as its parent key), `[]`, plain scalars, double-quoted scalars (JSON
escapes), single-quoted scalars (`''`), `#` comments, one optional leading `---`. A plain `true` or `false` is a boolean, a plain
integer is an integer, a quoted scalar is always a string; any other plain scalar is a string (`yes`, `5.0`). A file of the
subset is also YAML, so any YAML tool reads it. The files of `v2/` are written by `v2/_gen/gen-v2.mjs`, which writes nothing else.
