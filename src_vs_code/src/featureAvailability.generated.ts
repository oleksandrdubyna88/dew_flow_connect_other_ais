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
export const CONSULTING: readonly Runtime[] = ['codex', 'claude', 'antigravity', 'local', 'api'];

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
    source: 'probe',
    levels: [],
    measuredWith: '',
    note: "The Team server judges it per vendor (contract 2, PLAN_one_model_catalog.md E2.5): applied where its runtime lists the level, lowered to the operator's cap, or dropped — and the reviewer's note says which. A contract-1 server applies none.",
  },
  {
    runtime: 'api',
    source: 'probe',
    levels: [],
    measuredWith: '',
    note: "The model's probe report (coai-mcp --probe-api, ApiCapabilities.EffortLevels), judged per model.",
  },
];

/** Whether a runtime has a thinking switch (D12): `probe` asks the model's report, the others say why there is none. */
export type ThinkingSource = 'probe' | 'unmeasured' | 'none';

/** One runtime's thinking switch — and, always, the sentence a card shows when there is none. */
export interface ThinkingRow {
  readonly runtime: Runtime;
  readonly source: ThinkingSource;
  readonly note: string;
}

/** One row per runtime. */
export const THINKING: readonly ThinkingRow[] = [
  {
    runtime: 'codex',
    source: 'unmeasured',
    note: 'No flag that switches codex’s thinking off has been shown taking effect, so the card says so rather than drawing a switch.',
  },
  {
    runtime: 'gemini',
    source: 'none',
    note: 'The retired Gemini CLI runtime; a row saved on it is moved to antigravity on read.',
  },
  {
    runtime: 'claude',
    source: 'none',
    note: 'Claude’s depth is its effort (--effort); there is no separate thinking switch.',
  },
  {
    runtime: 'antigravity',
    source: 'none',
    note: 'The model id carries its level (gemini-3.7-flash-high); there is no switch.',
  },
  {
    runtime: 'local',
    source: 'unmeasured',
    note: 'A local engine’s thinking follows its effort setting; a separate switch has not been measured.',
  },
  {
    runtime: 'remote',
    source: 'unmeasured',
    note: 'A Team server row carries no thinking switch: contract 2 carries effort and a system prompt only.',
  },
  {
    runtime: 'api',
    source: 'probe',
    note: 'The model’s probe report says whether its thinking can be switched off (thinkingSwitchable), per model.',
  },
];
