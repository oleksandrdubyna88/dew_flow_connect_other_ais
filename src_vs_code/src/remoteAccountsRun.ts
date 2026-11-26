import type { CopyDecision } from './copyText';
import type { AccountsCommand } from './remoteAccountsMessages';

/**
 * What the host DOES with a checked command of the accounts tab (E2.S3, contract C5). Pure over its seams, so a test can answer every question
 * with Escape and see that nothing moved.
 *
 * <p><b>Two commands widen what is read, and neither decides.</b> `allow` only raises the ONE permission question (`askPermission`) and
 * `read-again` only raises the change question of that host (`askChange`, whose *Read this host again* is the one button that lifts a refusal):
 * what the person answers there, and nothing in this module, writes a grant or lifts a refusal. The three that narrow, `refuse`, `stop` and
 * `withdraw`, act at once. `copy-install` hands the copier the decision the message check made and nothing else.</p>
 */
export interface RunSeams {
  readonly refresh: () => Promise<unknown>;
  readonly stop: () => Promise<unknown>;
  readonly start: () => Promise<unknown>;
  /** The ONE question; it writes a grant only when the person chooses *Allow reading*. */
  readonly askPermission: () => Promise<unknown>;
  readonly withdraw: () => Promise<unknown>;
  readonly saveWaiting: () => Promise<unknown>;
  /** *Not this host*: a refusal, written at once. */
  readonly refuse: (host: string) => Promise<unknown>;
  /** The change question of one host. */
  readonly askChange: (host: string) => Promise<unknown>;
  readonly copy: (decision: CopyDecision) => Promise<unknown>;
}

type Run<K extends AccountsCommand['kind']> = (command: Extract<AccountsCommand, { kind: K }>, seams: RunSeams) => Promise<unknown>;

/** What each command does, by its kind: one seam each, and `ready` none. */
const RUN: { readonly [K in AccountsCommand['kind']]: Run<K> } = {
  ready: async () => undefined,
  refresh: (_command, seams) => seams.refresh(),
  stop: (_command, seams) => seams.stop(),
  start: (_command, seams) => seams.start(),
  allow: (_command, seams) => seams.askPermission(),
  withdraw: (_command, seams) => seams.withdraw(),
  'save-now': (_command, seams) => seams.saveWaiting(),
  refuse: (command, seams) => seams.refuse(command.host),
  'read-again': (command, seams) => seams.askChange(command.host),
  'copy-install': (command, seams) => seams.copy(command.decision),
};

/** Runs one command. `ready` is answered by the panel and never comes here. */
export async function runAccountsCommand(command: AccountsCommand, seams: RunSeams): Promise<void> {
  await (RUN[command.kind] as (one: AccountsCommand, from: RunSeams) => Promise<unknown>)(command, seams);
}
