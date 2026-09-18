import * as vscode from 'vscode';
import { PROBE_CAP_MS, probeClaudeModels, probeSucceeded, probeToKeep } from './claudeProbe';
import { PROBE_FILE, parseProbe, writeProbe } from './claudeProbeFile';
import { ProbeResult, stillGood } from './claudeModels';
import { askVersion, capture } from './versionProbe';
import { claudeExecutableFor, claudeIsWanted, mayAsk } from './claudeCli';
import { ConsultSettings } from './consultSettings';
import { Vendor } from './vendors';
import { unquoted } from './cliVersions';
import { ViewHandle } from './viewHandle';
import { writeFileAtomically } from './atomicFile';

/**
 * What this machine knows about the Claude CLI's models, and how it finds out again.
 *
 * <p><b>The first extraction from `panelProvider.ts`</b>, which was 4 022 lines against a ceiling of
 * 800 — one class holding roughly forty fields and ninety methods. The plan for that series
 * (`todo/PLAN_the_panel_provider_is_too_big.md`) chose this cluster to go first and alone, because it
 * is the one that tests the METHOD at the lowest cost of being wrong: seven fields nothing else
 * touched and five methods that called only each other.</p>
 *
 * <p><b>Measured before it moved</b>, which is what made it first: of the `this.` references in those
 * 162 lines, every one was either a field of this cluster or one of exactly THREE things outside it —
 * `dataDir`, `render` and `held`. Those three are the whole seam, and they are taken in under their
 * own names. The panel's own surface is two lines of `render`: `claudeProbe: …answer(vendors,
 * consult)` and `askingClaude: …looking`, in that order, which is preserved because the first may
 * start a refresh that sets the second.</p>
 *
 * <p><b>No name changed, and that is the point rather than laziness.</b> `scripts/prove-move.mjs`
 * matches whole lines against the file they came from, so every renamed field or collaborator turns a
 * moved line into residue a reviewer has to check by hand. Keeping `this.claudeProbe`,
 * `this.dataDir`, `this.render` and `this.held` exactly as `panelProvider.ts` had them leaves the
 * bodies byte-identical: the first draft reached them through an `around` object and made **eight**
 * lines residue for nothing. What residue remains is the scaffolding a class needs — the interface,
 * the declaration, the constructor, the getter — and that is what the pull request justifies line by
 * line.</p>
 */

/**
 * The three things this cache needs from the panel it lives in, and nothing else.
 *
 * <p><b>Named exactly as the panel names them</b>, which is not laziness: it is what keeps every line
 * that uses them a proven MOVE. `scripts/prove-move.mjs` matches whole lines against the file they
 * came from, so reaching them through an `around` object would have made eight lines residue for a
 * reviewer to check by hand; these names make them byte-identical. The `held` handle is
 * SHARED rather than copied — it is the same `ViewHandle` the panel holds, so a window that goes is
 * gone here at the same instant.</p>
 */
export interface ProbeSurroundings {
  /** Where the kept answer lives. */
  readonly dataDir: vscode.Uri;
  /** Repaint — the panel says what it is doing while a probe runs, and says the answer when it lands. */
  readonly render: () => Promise<void>;
  /** The panel's own view handle. Four candidates is up to a hundred seconds of BILLED requests. */
  readonly held: ViewHandle<vscode.WebviewView>;
}

export class ClaudeProbeCache {
  private claudeProbe: ProbeResult | undefined = undefined;

  private claudeProbeRead = false;

  private claudeProbeInFlight = false;

  private askingClaude = false;

  private claudeAskedFor = '';

  private claudeCliVersion = '';

  private claudeProbeFailedAt = 0;

  private readonly dataDir: vscode.Uri;

  private readonly render: () => Promise<void>;

  private readonly held: ViewHandle<vscode.WebviewView>;

  constructor(around: ProbeSurroundings) {
    this.dataDir = around.dataDir;
    this.render = around.render;
    this.held = around.held;
  }

  /** Whether a probe is running, for the sentence the panel shows while it is. */
  get looking(): boolean {
    return this.askingClaude;
  }

  answer(vendors: readonly Vendor[], consult: ConsultSettings): ProbeResult | undefined {
    const wanted = claudeIsWanted(vendors, consult);
    // EDGE-TRIGGERED. It used to start a refresh on every render, and the refresh repainted from a
    // `finally` whatever it had found — so a fresh cache still caused a repaint, which started
    // another refresh, which spawned another `--version`, for ever. A render now asks at most once
    // per SETTLING: the flag is cleared only when a probe genuinely could not be judged fresh, and
    // the executable is part of the key so repointing the CLI is still noticed. (Blocking, codex.)
    const executable = claudeExecutableFor(vendors, consult);
    if (wanted && mayAsk(this.claudeAskedFor, executable, Date.now(), this.claudeProbeFailedAt)) {
      this.claudeAskedFor = executable;
      this.claudeProbeFailedAt = 0;
      this.refreshClaudeProbe(vendors, consult).then(undefined, (error: unknown) => {
        // A catch-all at the detached edge, per the try/catch rule: this promise is deliberately
        // not awaited, so without one a failure here is an unhandled rejection and nothing else.
        console.error('ConnectOtherAIs: the Claude model probe failed', error);
      });
    }

    return this.claudeProbeToShow();
  }

  /**
   * The answer a dropdown may draw on — which is not always the one on disk.
   *
   * <p>An answer whose CLI version no longer matches the binary this machine runs is EVIDENCE ABOUT
   * ANOTHER BINARY. Keeping it in memory is right (it is what a failed probe falls back to), but
   * presenting it as confirmed would label a family verified from a record a different CLI wrote —
   * and a person choosing that alias can then silently get the default. It is withheld until a probe
   * confirms it against the version actually installed. (codex SecurityReliability, this round.)</p>
   */
  private claudeProbeToShow(): ProbeResult | undefined {
    const probe = this.claudeProbe;
    if (probe === undefined || probe.cliVersion !== this.claudeCliVersion) {
      return undefined;
    }

    // And the BINARY, not only the version. A reviewer row and a consultant can point at two
    // different installations of one version, signed into two different accounts — so a record that
    // names another executable is evidence about another account's models. A record written before
    // that field existed names none, and is trusted, because nothing else about it says otherwise.
    return (probe.executable ?? '') === '' || probe.executable === this.claudeAskedFor ? probe : undefined;
  }

  /**
   * One probe at a time, a repaint when it lands, and a *looking* state for as long as it runs.
   *
   * <p>The freshness question is asked against the CLI's OWN version, which is itself a process
   * spawn — so it is asked here rather than in the render, and a machine with no Claude CLI answers
   * an empty version and is never probed at all.</p>
   */
  private async refreshClaudeProbe(vendors: readonly Vendor[], consult: ConsultSettings): Promise<void> {
    if (this.claudeProbeInFlight) {
      return;
    }
    this.claudeProbeInFlight = true;
    // Whether anything CHANGED, so a no-op run repaints nothing. A repaint that changed nothing was
    // what closed the loop above into an endless one.
    let moved = false;
    // Both read in the `finally`, which is the only place that sees a THROW as well as a return.
    // Dating the failure inside the try meant an exception left nothing dated, so the edge trigger
    // never asked again for the rest of the session — the very hole the dating exists to close.
    let cliVersion = '';
    let succeeded = false;
    try {
      if (!this.claudeProbeRead) {
        this.claudeProbeRead = true;
        this.claudeProbe = await this.readClaudeProbe();
        moved = this.claudeProbe !== undefined;
      }
      const executable = claudeExecutableFor(vendors, consult);
      cliVersion = await askVersion(executable);
      // A CLI that will not say its version is not a transient failure — it is not installed at
      // this path — so it is NOT dated for a retry. Re-asking a binary that is not there every ten
      // minutes would be a process spawn a person never asked for, for ever.
      moved = moved || cliVersion !== this.claudeCliVersion;
      this.claudeCliVersion = cliVersion;
      if (cliVersion.length === 0 || stillGood(this.claudeProbe, cliVersion, Date.now())) {
        // Nothing to do, which is not a failure: an absent CLI must not be re-asked every ten
        // minutes for ever, and a fresh answer is the answer.
        succeeded = true;

        return;
      }
      moved = true;

      // SAID before it is started. The alternative is tens of seconds of a dropdown that looks
      // finished, which is exactly how a person chooses from a list that was about to change.
      this.askingClaude = true;
      await this.render();
      const found = await probeClaudeModels(
        {
          run: (args) => capture(unquoted(executable), args, false, PROBE_CAP_MS, () => this.held.view === undefined),
          cliVersion: async () => cliVersion,
          executable,
          now: () => Date.now(),
        },
        undefined,
        // Asked before each candidate. Four of them is up to a hundred seconds of BILLED requests,
        // and a window that has gone will not read the answer. (gemini, this round.)
        () => this.held.view === undefined,
      );
      // A run that learned nothing keeps the previous answer: an account whose allowance is spent
      // is a state this installation is really in, and it must not empty anybody's dropdown.
      this.claudeProbe = probeToKeep(found, this.claudeProbe);
      succeeded = probeSucceeded(found);
      if (this.claudeProbe !== undefined) {
        await this.keepClaudeProbe(this.claudeProbe);
      }
    } finally {
      this.claudeProbeInFlight = false;
      // A run that did not succeed is dated so it can be tried again shortly — including one that
      // THREW, which is what a `finally` sees and the try did not. A CLI that could not even say its
      // version is left alone: it is not installed at that path, and re-asking a binary that is not
      // there every ten minutes is a process spawn nobody asked for.
      if (!succeeded && cliVersion.length > 0) {
        this.claudeProbeFailedAt = Date.now();
      }
      const wasLooking = this.askingClaude;
      this.askingClaude = false;
      // Whatever happened, the panel stops saying it is looking. Cleared here rather than at the
      // end, because a throw would otherwise leave that sentence on screen for good — and repainted
      // ONLY when something a person can see actually changed, because an unconditional repaint
      // here is what made every render start another probe.
      if (moved || wasLooking) {
        await this.render();
      }
    }
  }

  /** The answer this machine kept, or nothing at all — an unreadable file is not an answer. */
  private async readClaudeProbe(): Promise<ProbeResult | undefined> {
    try {
      const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(this.dataDir, PROBE_FILE));

      return parseProbe(new TextDecoder().decode(bytes));
    } catch {
      return undefined; // never asked here, or the data directory has moved
    }
  }

  /**
   * Keep it for the week.
   *
   * <p>Atomically, like every other writer here: a window killed mid-write would otherwise leave a
   * truncated file that the next launch reads as no answer, which costs four requests in silence.
   * A disk that refuses is not worth a message — the answer is still live in this process, and the
   * only cost is asking again next week.</p>
   */
  private async keepClaudeProbe(probe: ProbeResult): Promise<void> {
    try {
      await vscode.workspace.fs.createDirectory(this.dataDir);
      await writeFileAtomically(vscode.Uri.joinPath(this.dataDir, PROBE_FILE).fsPath, writeProbe(probe));
    } catch (error) {
      // The list on screen is correct either way — this costs a re-probe next week, not an answer.
      // But a persistence failure that says nothing is one nobody can act on, and this product's
      // own store already names the path it could not write. (codex Conventions, this round.)
      console.error(
        `ConnectOtherAIs: the Claude model probe could not be kept at ${vscode.Uri.joinPath(this.dataDir, PROBE_FILE).fsPath}`,
        error,
      );
    }
  }
}
