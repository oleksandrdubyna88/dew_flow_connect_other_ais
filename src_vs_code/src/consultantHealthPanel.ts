import * as vscode from 'vscode';

import { type CheckRecord, parseCheckFile } from './consultantHealth';
import { runConsultantCheck } from './consultantCheckRun';
import { ConsultantHealthHost } from './consultantHealthHost';
import { type FileRead, healthPaths } from './consultantHealthRead';
import { type ConsultantHealthState, reportedAbout, reportedSnippet } from './consultantHealthState';
import { ConsultantHealthWatcher } from './consultantHealthWatcher';
import { CONSULTANTS_CAP_MS, ConsultantsProbe } from './consultantsProbe';
import { type HealthSide, healthSides, thisSideLabel } from './consultantSides';
import { textCopier } from './copyText';
import { alsoWatchDataDirectories } from './escalationWatcher';
import { watchGlob } from './fileWatch';
import { serverPath } from './installer';
import { notify, notifyAndAsk } from './notify';
import { serverRun } from './roundsDbRead';

/**
 * The Consultant tab's health, composed with `vscode` — the one place the probe, the watcher, the check flow and the
 * copy meet the editor (E5.4 of `research/PLAN_the_consultant_works_on_every_vendor.md`).
 *
 * <p>Wiring only, so `panelProvider.ts` holds none of it: every decision lives in a module a test reaches —
 * `consultantsProbe.ts` (never awaited by a render), `consultantHealthWatcher.ts` (own side watched, every side polled),
 * `consultantHealthHost.ts` (the paid-call confirmation and the optimistic flag), `consultantCheckRun.ts` (the exit
 * codes and the kill cap) and `consultantHealthState.ts` (what each row shows). What is here is the ports: the spawn
 * through `serverRun` (the door that carries this window's data directory), files through `vscode.workspace.fs`, a
 * modal through the `notifyAndAsk` funnel (so the asking and the answer reach the ledger), the clipboard and the status bar.</p>
 *
 * <p>Started on the first {@link stateWhile} that is SHOWING — the Settings tab visible with its Consultant tab chosen —
 * rather than at activation, and PAUSED by any render that is not, and when that tab closes or is hidden ({@link pause}):
 * a window that is not showing the Consultant tab polls nobody's store and spawns no `--consultants` (the whole-branch
 * review, N2; `consultantTabShowing` decides).</p>
 */

/** What the panel hands in: where this window keeps its data, where the server is installed, and how to repaint. */
export interface HealthPanelDeps {
  readonly dataDir: vscode.Uri;
  readonly storage: vscode.Uri;
  readonly render: () => void;
}

export class ConsultantHealthPanel {
  private readonly probe: ConsultantsProbe;

  private readonly watcher: ConsultantHealthWatcher;

  private readonly host: ConsultantHealthHost;

  constructor(private readonly deps: HealthPanelDeps) {
    this.probe = new ConsultantsProbe({
      executable: () => this.executable(),
      run: (executable, args) => serverRun(executable)(args, CONSULTANTS_CAP_MS),
      now: Date.now,
      render: deps.render,
      log: (message) => console.warn(message),
    });
    this.watcher = new ConsultantHealthWatcher({
      sides: () => this.sides(),
      read: readFile,
      now: Date.now,
      watch: watchHealth,
      every: everyMs,
    });
    this.watcher.onChanged = deps.render;
    // Only this side's server can settle this side's check by its lock, so a state that moved is asked of it again.
    this.watcher.onOwnChecksChanged = () => { this.probe.forget(); deps.render(); };
    this.host = new ConsultantHealthHost({
      // Through the notify funnel, like every modal here: the asking and the answer are written to the ledger.
      confirm: async (title, detail, go) => (await notifyAndAsk({
        as: 'warning', class: 'confirmation', source: 'consultant', code: 'paid-consultant-check', modal: true, title, detail, action: go,
      })) === go,
      runCheck: (kind) => this.runCheck(kind),
      settled: () => { this.probe.forget(); void this.watcher.refresh(); deps.render(); },
      // Through the funnel, so the landing is recorded and a screen reader hears it — the page's lines cannot be a live region.
      announce: (kind, sentence) => {
        void notify({ as: 'information', class: 'outcome', source: 'consultant', code: 'consultant-check-landed', subject: kind, title: sentence });
      },
      render: deps.render,
      nowUtc: () => new Date().toISOString(),
      copier: textCopier({
        writeText: (text) => Promise.resolve(vscode.env.clipboard.writeText(text)),
        say: (message, forMs) => vscode.window.setStatusBarMessage(message, forMs),
      }),
    });
  }

  /**
   * What every row's health block draws now while the Consultant tab is SHOWING — never a wait: the probe and the watcher
   * land on their own and repaint — and nothing at all, with the watcher paused, while it is not.
   */
  stateWhile(showing: boolean): ConsultantHealthState | undefined {
    if (!showing) {
      this.pause();

      return undefined;
    }

    return this.state();
  }

  private state(): ConsultantHealthState {
    // Idempotent: the first render that asks starts it, and the first after a pause resumes it.
    this.watcher.start();
    const sides = this.sides();
    const own = sides[0];

    return {
      thisSide: {
        label: own?.label ?? '',
        probe: this.probe.shown(),
        files: own === undefined ? undefined : this.watcher.side(own.dir),
        checking: this.host.checking(),
        runs: this.host.runs(),
      },
      otherSides: sides.filter((side) => side.kind === 'other').map((side) => ({ label: side.label, dir: side.dir, files: this.watcher.side(side.dir) })),
      nowMs: Date.now(),
    };
  }

  /** A press of Check for one caller kind. */
  check(kind: string): Promise<void> {
    return this.host.check(kind, reportedAbout(this.probe.shown(), kind));
  }

  /** A press of Copy under one caller kind's allow rule; `told` tells the page that was pressed. */
  async copySnippet(kind: string, told: (kind: string) => void): Promise<void> {
    await this.host.copySnippet(kind, reportedSnippet(this.probe.shown(), kind), told);
  }

  /** The Settings tab closed or was hidden: stop polling every side (a WSL share can wake its distribution); the next showing render resumes. */
  pause(): void {
    this.watcher.pause();
  }

  dispose(): void {
    this.watcher.dispose();
  }

  private executable(): string {
    return serverPath(this.deps.storage)?.fsPath ?? '';
  }

  private sides(): readonly HealthSide[] {
    const label = thisSideLabel(vscode.env.remoteName, process.env['WSL_DISTRO_NAME'] ?? '', process.platform);

    return healthSides({ label, dir: this.deps.dataDir.fsPath }, alsoWatchDataDirectories(), process.platform);
  }

  private runCheck(kind: string): ReturnType<typeof runConsultantCheck> {
    const executable = this.executable();
    if (executable.length === 0) {
      return Promise.resolve({ kind: 'crashed', why: 'the MCP server is not installed on this side' });
    }

    return runConsultantCheck(kind, {
      run: (args, capMs, stop) => serverRun(executable, stop)(args, capMs),
      readState: () => ownCheck(this.deps.dataDir.fsPath, kind),
      nowMs: Date.now,
      every: everyMs,
    });
  }
}

/** One file, as the read port wants it: bytes, absent (`FileNotFound`), or failed — the error's own code tells them apart. */
async function readFile(path: string): Promise<FileRead> {
  try {
    return { kind: 'bytes', bytes: await vscode.workspace.fs.readFile(vscode.Uri.file(path)) };
  } catch (error: unknown) {
    return error instanceof vscode.FileSystemError && error.code === 'FileNotFound' ? { kind: 'absent' } : { kind: 'failed', why: whyOf(error) };
  }
}

function whyOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** This side's `<kind>.check.json` as it is now — what the check run tightens its kill cap from. */
async function ownCheck(dir: string, kind: string): Promise<CheckRecord | undefined> {
  const read = await readFile(healthPaths(dir).check(kind));
  const parsed = read.kind === 'bytes' ? parseCheckFile(read.bytes) : undefined;

  return parsed?.kind === 'found' ? parsed.value : undefined;
}

/** A glob over the data directory rather than a handle on a folder: `health/` may not exist until the first write. */
function watchHealth(dir: string, changed: () => void): () => void {
  return watchGlob(dir, 'consultations/health/*.json', changed);
}

function everyMs(ms: number, tick: () => void): () => void {
  const timer = setInterval(tick, ms);

  return () => clearInterval(timer);
}
