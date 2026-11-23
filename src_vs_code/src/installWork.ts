import type { Notice } from './notice';
import { failuresNotice, setupClients, type SetupOutcome, type SpawnServer } from './setupClients';

/**
 * The install module's work after the first `menuReady` (v2 E4, story s5-install-module): the binary check, `coai-mcp --setup-clients`, then the offer of a newer server.
 *
 * <p><b>Once per activation.</b> {@link InstallWork.start} runs the sequence one time however often it is asked (the sidebar asks after `menuReady`, the shell asks again
 * after the first command; both are the same run). The one thing that may run the mode again is a successful install or update in this window
 * ({@link InstallWork.serverInstalled}): the binary changed, and a new server may register itself differently — a run that finds nothing to do is silent.</p>
 *
 * <p><b>Nothing here throws and nothing here asks.</b> Each step is its own unit: one that fails is logged and the next one still runs. The only
 * thing a person is ever told is a client that was refused or failed — one notification — and the update offer, which is the old code's own question,
 * moved and not changed.</p>
 *
 * <p>Pure of the editor: every seam is in {@link InstallPorts}, so the order and the once-ness are tested without a host (`installWork.test.ts`).</p>
 */
export interface InstallPorts {
  /** Where this side's server binary is installed, or `undefined` when the release matrix has no build for this platform. */
  readonly binary: () => string | undefined;
  /** Whether a file is at that path now (`stat`, never a remembered version). */
  readonly exists: () => Promise<boolean>;
  /** Asks whether a newer server is published and offers it; calls `installed` when the person took it and it landed. Answers when the person has answered. */
  readonly offerUpdate: (installed: () => void) => Promise<void>;
  /** The spawn of that binary. */
  readonly spawn: (binary: string) => SpawnServer;
  /** The one funnel that tells a person something. */
  readonly say: (notice: Notice) => Promise<void>;
  /** The console: for what the person is not told. */
  readonly log: (message: string, error?: unknown) => void;
}

export class InstallWork {
  private started: Promise<void> | undefined;
  /** Runs of the mode follow one another: two never overlap, and a late one waits for the one before it. */
  private chain: Promise<void> = Promise.resolve();

  constructor(private readonly ports: InstallPorts) {}

  /** The whole sequence, once. Every later call answers the same promise. */
  start(): Promise<void> {
    this.started ??= this.firstRun();

    return this.started;
  }

  /** A server was installed or updated in this window: the mode runs again with the new binary, after any run still going. */
  serverInstalled(): Promise<void> {
    this.chain = this.chain.then(() => this.setUpClients());

    return this.chain;
  }

  private async firstRun(): Promise<void> {
    this.chain = this.chain.then(() => this.setUpClients());
    await this.chain;
    // The update offer comes second, and a person's answer to it (or none, for ever) does not hold the mode back.
    await this.step('the update offer', () => this.ports.offerUpdate(() => { void this.serverInstalled(); }));
  }

  private setUpClients(): Promise<void> {
    return this.step('--setup-clients', async () => {
      const binary = await this.installedBinary();
      if (binary !== undefined) {
        await this.tell(await setupClients(binary, this.ports.spawn(binary)));
      }
    });
  }

  /** This side's server binary when one is there to run, else `undefined`. */
  private async installedBinary(): Promise<string | undefined> {
    const binary = this.ports.binary();

    return binary !== undefined && await this.ports.exists() ? binary : undefined;
  }

  /** What a run came to, to the right audience: a failed client to the person (ONE notice), what cannot be read to the console, everything else to nobody. */
  private async tell(outcome: SetupOutcome): Promise<void> {
    if (outcome.kind === 'unread') {
      this.ports.log(`--setup-clients: ${outcome.why}`);
    } else if (outcome.kind === 'done' && outcome.failures.length > 0) {
      await this.ports.say(failuresNotice(outcome.failures));
    }
  }

  private async step(what: string, run: () => Promise<void>): Promise<void> {
    try {
      await run();
    } catch (error) {
      this.ports.log(`${what} failed`, error);
    }
  }
}
