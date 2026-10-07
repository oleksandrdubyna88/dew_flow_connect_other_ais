// GENERATED FILE — do not edit by hand.
//
// Written by `node scripts/generate-feature-availability.mjs` from the `fastMode` block of
// `shared/feature-availability.json` (todo/PLAN_fast_mode.md). Edit the seed and run the script;
// `generatedFilesAreCurrent.test.ts` fails if this file and the generator disagree. It imports nothing, on purpose.

/** Where a runtime's fast tier is (todo/PLAN_fast_mode.md): on every model, on the listed models, or none. */
export type FastSource = 'every-model' | 'models' | 'none';

/** One runtime's fast tier — and, always, why. */
export interface FastModeRow {
  readonly runtime: string;
  readonly source: FastSource;
  readonly models: readonly string[];
  readonly measuredWith: string;
  readonly note: string;
}

/** One row per runtime. */
export const FAST_MODE: readonly FastModeRow[] = [
  {
    runtime: 'codex',
    source: 'every-model',
    models: [],
    measuredWith: 'codex-cli 0.160.0',
    note: '-c service_tier=default (Off) or =fast (On), on codex’s OWN service only — a row on another endpoint has no codex tier. Measured 2026-10-07: the key is read and checked per model; a value the model does not advertise is dropped with a warning, never a refusal.',
  },
  {
    runtime: 'gemini',
    source: 'none',
    models: [],
    measuredWith: '',
    note: 'The retired Gemini CLI runtime; a row saved on it is moved to antigravity on read.',
  },
  {
    runtime: 'claude',
    source: 'models',
    models: ['opus', 'claude-opus-5-5', 'claude-opus-5', 'claude-opus-4-8'],
    measuredWith: 'Claude Code 2.1.289',
    note: '--settings with a one-key file, fastMode true or false (documented for claude -p, v2.1.205+), Opus 5.5, 5 and 4.8 only. Measured 2026-10-07: Off reports fast_mode_state off; On is held off where the account’s own preference does not allow it, and the run says so.',
  },
  {
    runtime: 'antigravity',
    source: 'none',
    models: [],
    measuredWith: '',
    note: 'No fast tier: the model id carries its level.',
  },
  {
    runtime: 'local',
    source: 'none',
    models: [],
    measuredWith: '',
    note: 'A local engine has no service tier.',
  },
  {
    runtime: 'remote',
    source: 'none',
    models: [],
    measuredWith: '',
    note: 'A Team server row carries no fast mode: its contract does not.',
  },
  {
    runtime: 'api',
    source: 'none',
    models: [],
    measuredWith: '',
    note: 'Not until a dialect’s tier is measured on a real call (xai first); the generic openai dialect serves every unnamed endpoint and gets none.',
  },
];
