import { AccountsRead } from '../remoteAccountsReply';
import { AccountsMode } from '../remoteAccountsScript';

/**
 * The frames of the accounts reply, built once for the tests of the parser (E1.S2, spec § A2.11.3 and § A2.11.4). A test never reads
 * the clock: every parser call passes `NOW`, and nothing here uses a random number.
 */

/** The moment every parser test reads at, unless a row says otherwise. */
export const NOW = Date.UTC(2026, 10, 19, 12, 0);

/** A frame of § A2.5 around these lines. */
export const frame = (...lines: readonly string[]): string => ['COAI-ACCOUNTS 1', ...lines, 'COAI-ACCOUNTS-END', ''].join('\n');

export const ok = (stdout: string): { readonly code: number; readonly stdout: string } => ({ code: 0, stdout });

const DOT = '·';

/** The three measured usage lines. */
export const LINES: readonly string[] = [
  `Current session: 56% used ${DOT} resets Nov 19, 3pm (UTC)`,
  `Current week (all models): 77% used ${DOT} resets Nov 22, 3am (UTC)`,
  `Current week (Fable): 49% used ${DOT} resets Nov 22, 2:59am (UTC)`,
];

/** The reply of § A2.5 as lines (the Claude account of the measured host, made anonymous). */
export const GOOD_LINES: readonly string[] = [
  'COAI-JQ found',
  'COAI-VENDOR claude found',
  ':email someone@example.com',
  ':org Example Org',
  ':plan team',
  ':version 2.1.326',
  'COAI-USAGE claude read',
  ':turns 0',
  ...LINES.map((line) => `:line ${line}`),
  ':unmatched 0',
  'COAI-VENDOR codex found',
  ':sign-in-file present',
  ':plan prolite',
  'COAI-USAGE codex read',
  ':window 14 10080 1795518850',
  ':as-of 2026-11-17T19:32:36.474Z 1794938000',
  'COAI-VENDOR agy found',
  ':version 1.5.12',
  ':sign-in-file present',
  'COAI-USAGE agy none',
];

/** `GOOD` is asserted ONCE (in the reply test) to be read, so each negative row changes one thing of a reply the parser accepts. */
export const GOOD = frame(...GOOD_LINES);

/** A reply of the mode `claude-usage` whose Claude usage holds exactly these `:line` texts; the two other vendors are absent. */
export const claudeLines = (texts: readonly string[], turns = 0, unmatched = 0): string => frame(
  'COAI-JQ found',
  'COAI-VENDOR claude found',
  'COAI-USAGE claude read',
  `:turns ${turns}`,
  ...texts.map((text) => `:line ${text}`),
  `:unmatched ${unmatched}`,
  'COAI-VENDOR codex absent',
  'COAI-VENDOR agy absent',
);

/** A reply whose Codex part holds these data lines under `COAI-USAGE codex read`; Claude and agy are absent. */
export const codexLines = (...data: readonly string[]): string => frame(
  'COAI-JQ found',
  'COAI-VENDOR claude absent',
  'COAI-VENDOR codex found',
  ':sign-in-file present',
  'COAI-USAGE codex read',
  ...data,
  'COAI-VENDOR agy absent',
);

export const FILE_TIME = 1794938000;
export const RECORD_TIME = '2026-11-17T19:32:36.474Z';

/** The `read` arm of an answer, or a failure that says what it was instead. */
export function readOf(answer: AccountsRead): Extract<AccountsRead, { kind: 'read' }> {
  if (answer.kind !== 'read') {
    throw new Error(`the answer is unreadable: ${answer.why}`);
  }

  return answer;
}

export type { AccountsMode };
