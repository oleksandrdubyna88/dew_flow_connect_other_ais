// The perf marks of the cold-start measurement (v2 extension.md § 2 "Measurement", epic E2 story S2) and the one command that
// reads them. This file is part of the entry bundle, so it imports nothing but the TYPE of `vscode`, and everything it does at
// run time is a timestamp pushed onto a list. `registerMeasure` is registration only: with COAI_MEASURE unset it registers
// nothing at all, with COAI_MEASURE=1 it registers `coai.__measure` and that command's body runs only when somebody calls it.
//
// The list lives on `globalThis` under a registered symbol, not in this module's own state: the entry bundle and the module
// bundles each carry their own copy of this file, and a mark made in one must be readable through the command registered in
// the other. A mark is the wall-clock moment as `performance.timeOrigin + performance.now()` — milliseconds since 1970 with
// sub-millisecond precision — so a driver in the same host (or on the same machine) can put its own clock beside it.
import type * as vscode from 'vscode';

/** The command that answers the marks; registered only when COAI_MEASURE=1, in every extension mode. */
export const MEASURE_COMMAND = 'coai.__measure';

/**
 * The marks the shell makes, in the order it makes them. `activateStart` and `activateReturn` bound M1 (the entry's own time inside `activate`);
 * `eval` to `activateStart` is the host's own delay before it calls `activate` (`hostDelay`, recorded, no limit); `ready` ends M2; `firstData` ends M3.
 * `moduleRequire` / `moduleEval` / `moduleLoaded` bracket the load of a lazy module's bundle; `legacyBody` and `legacyActivate*` are the legacy module's
 * (`shell/modules/legacy.ts`). The old code's own marks (`src/measureMarks.ts`, `OLD_MARKS`) go through the same `mark`, so the name is a plain string here:
 * this file may not import `src/`.
 */
export type MarkName = string;

export interface Mark {
  readonly name: string;
  /** Milliseconds since 1970 (`performance.timeOrigin + performance.now()`). */
  readonly at: number;
}

/** What `coai.__measure` answers. `host` says WHERE the extension host runs: a measurement must name its location. */
export interface MeasureReport {
  readonly schema: 1;
  readonly marks: readonly Mark[];
  readonly host: {
    /** `vscode.env.remoteName`: `'wsl'`, `'ssh-remote'`, ... and `null` for a local extension host. */
    readonly remoteName: string | null;
    readonly uiKind: number;
    readonly platform: string;
    readonly vscodeVersion: string;
    readonly appName: string;
  };
}

/** A view that is re-resolved and a panel that repaints all day must not grow this list without end: a few of each mark are kept. */
const MAX_PER_NAME = 8;

const KEY = Symbol.for('coai.measure.marks');

/** The wall-clock moment now, in milliseconds since 1970. */
export const now = (): number => performance.timeOrigin + performance.now();

function store(): Mark[] {
  const holder = globalThis as unknown as Record<symbol, Mark[] | undefined>;
  let marks = holder[KEY];
  if (marks === undefined) {
    marks = [];
    holder[KEY] = marks;
  }

  return marks;
}

/** Records one mark; every call adds one, and the reader picks the one it wants (the first after its own clock started). */
export function mark(name: MarkName): void {
  const marks = store();
  if (marks.filter((one) => one.name === name).length < MAX_PER_NAME) {
    marks.push({ name, at: now() });
  }
}

/** Every mark so far, oldest first. */
export const marksSoFar = (): readonly Mark[] => [...store()];

// MODULE EVALUATION START: the first statement this bundle runs, because `entry.ts` imports this file before anything else.
// Only the first copy is `eval` — a module bundle evaluates its own copy of this file when it is first required, and that is `moduleEval`,
// the start of that bundle's evaluation (its first import must be this file).
mark(store().some((one) => one.name === 'eval') ? 'moduleEval' : 'eval');

/** The api this file needs; the entry passes the `vscode` namespace. */
type Api = Pick<typeof vscode, 'commands' | 'env' | 'version'>;

/**
 * The registrations `activate` adds: none, unless `env.COAI_MEASURE` is `1`. Registering is all this does: the command's
 * body reads the editor's state only when a driver calls it.
 */
export function registerMeasure(api: Api, env: Readonly<Record<string, string | undefined>>): vscode.Disposable[] {
  if (env['COAI_MEASURE'] !== '1') {
    return [];
  }

  return [api.commands.registerCommand(MEASURE_COMMAND, (): MeasureReport => ({
    schema: 1,
    marks: marksSoFar(),
    host: {
      remoteName: api.env.remoteName ?? null,
      uiKind: api.env.uiKind,
      platform: process.platform,
      vscodeVersion: api.version,
      appName: api.env.appName,
    },
  }))];
}
