import { serverEnv } from './dataDir';
import type { Notice } from './notice';
import { capture, type Captured } from './versionProbe';

/**
 * `coai-mcp --setup-clients`, run once per activation after the sidebar's first `menuReady` (v2 E4, story s5-install-module).
 *
 * <p>The SERVER does the work: it finds the AI clients on this side, writes its own entry into each one's configuration, and finishes an interrupted earlier
 * update from its manifest (`v2/epoch-1/e3-server-cleanup-install/stories/s6-install/SPEC.md`). The extension only runs the mode and reads its ONE JSON object.
 * It is never a question to the person (auto mode): `changed` and `unchanged` are silent, and every client that was `refused` or `failed` is ONE notification.</p>
 *
 * <p><b>The binary is named explicitly</b> (`--binary <path>`): in epoch 1 the stable path of the server is the one this extension installed, so the entry it
 * registers must be that file and not whatever path the server computes for itself.</p>
 *
 * <p><b>Exit 64 is "this binary has never heard of the mode"</b>, and only that (`.agents/PROJECT.md`). A binary from before the mode existed answers it, and the
 * answer is silence: an older server is an ordinary state of a machine, and the panel already offers the update. Every other outcome the extension cannot read
 * (no output, not JSON, a code the contract does not name) is the console's, not the person's: the extension must not invent a failure the server did not report.</p>
 */

/** What a spawn of the server answers: its exit code and everything it wrote. The same shape `roundsDbRead.ts` calls `Run`, spelled here so this file imports none of it. */
export type SpawnServer = (args: readonly string[], capMs: number) => Promise<Captured>;

/** `64`: the mode is unknown to this binary. */
export const EX_UNKNOWN_MODE = 64;

/** The server's own vendor commands have a 30 s limit each and there can be three clients; the whole run gets a generous ceiling and no more. */
export const SETUP_CLIENTS_CAP_MS = 120_000;

/** The arguments of the one run: the mode, and the binary the entries must name. */
export function setupClientsArgs(binary: string): readonly string[] {
  return ['--setup-clients', '--binary', binary];
}

/** The real spawn: the server binary, no shell, the data directory the window chose in its environment (as every other spawn of it). */
export function spawnServer(binary: string): SpawnServer {
  return (args, capMs) => capture(binary, [...args], false, capMs, undefined, serverEnv());
}

/** One client the server could not set up. */
export interface ClientFailure {
  readonly cli: string;
  readonly result: 'refused' | 'failed';
  /** The server's own sentence, shortened; empty when it gave none. */
  readonly reason: string;
}

/** What one run came to. */
export type SetupOutcome =
  /** Exit 64: a binary from before the mode existed. Silent. */
  | { readonly kind: 'old-binary' }
  /** The server answered. `failures` is empty when every client is `changed`, `unchanged`, `absent` or `unmeasured`. */
  | { readonly kind: 'done'; readonly failures: readonly ClientFailure[] }
  /** The run could not be read. The console is told; the person is not. */
  | { readonly kind: 'unread'; readonly why: string };

/** The longest a client's reason is shown: a reason is a sentence, and a server that wrote a page must not fill a toast with it. */
const REASON_MAX = 200;

/** Exit codes after which the report is read for its failures; 73 is read as one failure even when the report names none (the backup was not written and nothing changed). */
const NOT_WRITTEN = 73;

/** Codes of the contract that are not failures of a client: no client found (69) and another run holding the lock (75). */
const SILENT_CODES: ReadonlySet<number> = new Set([0, 69, 75]);

/** The text parsed, or `undefined` when it is not JSON. */
function parsed(output: string): unknown {
  try {
    return JSON.parse(output);
  } catch {
    return undefined;
  }
}

/** The `clients` array of a report, or `undefined` when the value is not a report. */
function clientsOf(report: unknown): readonly unknown[] | undefined {
  const clients = typeof report === 'object' && report !== null ? (report as { clients?: unknown }).clients : undefined;

  return Array.isArray(clients) ? clients as unknown[] : undefined;
}

/** What one client of a report says about itself, whatever shape it really has. */
interface ClientFields {
  readonly cli?: unknown;
  readonly result?: unknown;
  readonly reason?: unknown;
}

const asFields = (client: unknown): ClientFields => (typeof client === 'object' && client !== null ? client as ClientFields : {});

/** A client's reason as a short sentence: text only, trimmed, and cut. */
const reasonOf = (reason: unknown): string => (typeof reason === 'string' ? reason.trim().slice(0, REASON_MAX) : '');

/** A client's name, or a plain word when the report gave none. */
const nameOf = (cli: unknown): string => (typeof cli === 'string' && cli.length > 0 ? cli : 'an AI client');

/** The failure a client is, or `undefined` when it is not one. */
function failureOf(client: unknown): ClientFailure | undefined {
  const fields = asFields(client);

  return fields.result === 'refused' || fields.result === 'failed'
    ? { cli: nameOf(fields.cli), result: fields.result, reason: reasonOf(fields.reason) }
    : undefined;
}

/** The clients of a report that `refused` or `failed`, or `undefined` when the text is not a report. Never throws. */
export function failuresOf(output: string): readonly ClientFailure[] | undefined {
  const clients = clientsOf(parsed(output));

  return clients?.map(failureOf).filter((one): one is ClientFailure => one !== undefined);
}

/** Runs `--setup-clients` on `binary` once and says what it came to. Nothing here throws and nothing here asks anybody anything. */
export async function setupClients(binary: string, run: SpawnServer): Promise<SetupOutcome> {
  const { code, output } = await run(setupClientsArgs(binary), SETUP_CLIENTS_CAP_MS);

  return code === EX_UNKNOWN_MODE ? { kind: 'old-binary' } : outcomeOf(code, failuresOf(output));
}

/** What a run that was NOT exit 64 came to: the report's failures against the exit code, per the contract's code table. */
function outcomeOf(code: number, failures: readonly ClientFailure[] | undefined): SetupOutcome {
  if (failures === undefined) {
    return { kind: 'unread', why: code === -1 ? 'the server did not run or did not finish' : `the server exited ${code} and printed no report` };
  }

  return failures.length > 0 ? { kind: 'done', failures } : outcomeOfAnEmptyReport(code);
}

/** A report that names no failed client: silent for the codes that are not failures, one failure for 73, and unreadable for a code the contract does not name. */
function outcomeOfAnEmptyReport(code: number): SetupOutcome {
  if (SILENT_CODES.has(code)) {
    return { kind: 'done', failures: [] };
  }

  return code === NOT_WRITTEN
    ? { kind: 'done', failures: [{ cli: 'the AI clients', result: 'failed', reason: 'the backup could not be written, so nothing was changed' }] }
    : { kind: 'unread', why: `the server exited ${code} with a report that names no failed client` };
}

/** The ONE notification for every client that failed: each one named with the server's reason, and what to do. The code is a literal, as the scan requires. */
export function failuresNotice(failures: readonly ClientFailure[]): Notice {
  const named = failures.map((one) => (one.reason.length === 0 ? `${one.cli} (${one.result})` : `${one.cli} (${one.result}: ${one.reason})`)).join('; ');

  return {
    as: 'warning',
    class: 'failure',
    source: 'setupClients',
    code: 'clients-not-set-up',
    title: `coai-mcp could not set up ${failures.length === 1 ? 'one AI client' : `${failures.length} AI clients`}: ${named}`,
    cure: 'Fix what the reason says, then reload the window: coai-mcp tries again every time the extension starts.',
  };
}
