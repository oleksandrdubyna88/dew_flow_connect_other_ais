// GENERATED FILE — do not edit by hand.
//
// Written by `node scripts/generate-feature-availability.mjs` from `shared/feature-availability.json`
// (PLAN_one_model_catalog.md, D4). Edit the seed and run the script; `featureAvailability.test.ts` fails
// if this file and the seed disagree, and `generatedFilesAreCurrent.test.ts` fails if this file and the
// generator do.
import type { Runtime } from './models';

/** Where a runtime's legal efforts come from. */
export type EffortSource = 'list' | 'probe' | 'unmeasured' | 'none';

/** One runtime's efforts: the levels when `source` is `list`, and why when it is not. */
export interface EffortRow {
  readonly runtime: Runtime;
  readonly source: EffortSource;
  readonly levels: readonly string[];
  readonly measuredWith: string;
  readonly note: string;
}

/** The runtimes a consultant can run on, in the file's order. */
export const CONSULTING: readonly Runtime[] = ['codex', 'claude', 'antigravity', 'local'];

/** The runtimes a chat can speak to, in the file's order. */
export const CHAT: readonly Runtime[] = ['antigravity', 'claude', 'codex'];

/** One row per runtime. */
export const EFFORT: readonly EffortRow[] = [
  {
    runtime: 'codex',
    source: 'unmeasured',
    levels: [],
    measuredWith: '',
    note: 'codex-cli 0.160.0 reads model_reasoning_effort from its config, but no command short of a model turn names the legal values, and its binary carries several look-alike variant lists - so none is written here until one is observed taking effect (E2.2).',
  },
  {
    runtime: 'gemini',
    source: 'none',
    levels: [],
    measuredWith: '',
    note: 'The retired Gemini CLI runtime; a row saved on it is moved to antigravity on read (vendors.ts migrateRetired).',
  },
  {
    runtime: 'claude',
    source: 'list',
    levels: ['low', 'medium', 'high', 'xhigh', 'max'],
    measuredWith: 'claude --help, Claude Code 2.1.289, 2026-10-04: --effort <level> (low, medium, high, xhigh, max)',
    note: '',
  },
  {
    runtime: 'antigravity',
    source: 'none',
    levels: [],
    measuredWith: '',
    note: "The operator's ruling, 2026-10-04: no effort for Antigravity; the model id carries its level (gemini-3.7-flash-high).",
  },
  {
    runtime: 'local',
    source: 'probe',
    levels: [],
    measuredWith: '',
    note: "The local module's calibrated capabilities (ApiCapabilities.EffortLevels, COAI_LOCAL_REASONING_EFFORT).",
  },
  {
    runtime: 'remote',
    source: 'unmeasured',
    levels: [],
    measuredWith: '',
    note: 'A Team server carries no effort until its contract v2 (PLAN_one_model_catalog.md E2.5).',
  },
  {
    runtime: 'api',
    source: 'probe',
    levels: [],
    measuredWith: '',
    note: "The model's probe report (coai-mcp --probe-api, ApiCapabilities.EffortLevels), judged per model.",
  },
];
