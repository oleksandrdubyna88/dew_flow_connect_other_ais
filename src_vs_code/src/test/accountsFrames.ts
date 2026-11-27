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

/** The Claude account lines of the measured host, made anonymous: the lines the digest is made of are the first four. */
export const ACCOUNT_LINES: readonly string[] = [
  'COAI-VENDOR claude found',
  ':email someone@example.com',
  ':org Example Org',
  ':plan team',
];

/** The Claude usage lines of a reply to the usage mode (E3.S2, § R2.7). */
export const USAGE_READ_LINES: readonly string[] = [
  'COAI-USAGE claude read',
  ':turns 0',
  ...LINES.map((line) => `:line ${line}`),
  ':unmatched 0',
];

/** The measured quota of agy: the Gemini group part-used with a reset, the third-party group full with none (a dash). */
export const AGY_QUOTA_LINES: readonly string[] = [
  ':quota gemini weekly 60 1796283734',
  ':quota gemini 5h 92 1795813241',
  ':quota third-party weekly 100 -',
  ':quota third-party 5h 100 -',
];

/** The FIRST mode's reply (§ A2.5, revision 3): the three parts, Claude's usage `skipped`. */
export const GOOD_LINES: readonly string[] = [
  'COAI-JQ found',
  ...ACCOUNT_LINES,
  ':version 2.1.326',
  'COAI-USAGE claude skipped',
  'COAI-VENDOR codex found',
  ':sign-in-file present',
  ':account cox@example.test',
  ':plan prolite',
  'COAI-USAGE codex read',
  ':window 14 10080 1795518850',
  'COAI-VENDOR agy found',
  ':version 1.5.12',
  ':sign-in-file present',
  ':account agy@example.test',
  'COAI-USAGE agy read',
  ...AGY_QUOTA_LINES,
];

/** `GOOD` is asserted ONCE (in the reply test) to be read, so each negative row changes one thing of a reply the parser accepts. */
export const GOOD = frame(...GOOD_LINES);

/** The reply of the usage mode (`claude-usage`): the Claude part and nothing else. */
export const USAGE_GOOD_LINES: readonly string[] = ['COAI-JQ found', ...ACCOUNT_LINES, ':version 2.1.326', ...USAGE_READ_LINES];
export const USAGE_GOOD = frame(...USAGE_GOOD_LINES);

/** A reply of the mode `claude-usage` whose Claude usage holds exactly these `:line` texts; there is no other part (§ R2.7). */
export const claudeLines = (texts: readonly string[], turns = 0, unmatched = 0): string => frame(
  'COAI-JQ found',
  'COAI-VENDOR claude found',
  'COAI-USAGE claude read',
  `:turns ${turns}`,
  ...texts.map((text) => `:line ${text}`),
  `:unmatched ${unmatched}`,
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

/** A reply whose agy part holds these data lines under `COAI-USAGE agy read`; Claude and Codex are absent. */
export const agyLines = (...data: readonly string[]): string => frame(
  'COAI-JQ found',
  'COAI-VENDOR claude absent',
  'COAI-VENDOR codex absent',
  'COAI-VENDOR agy found',
  ':sign-in-file present',
  'COAI-USAGE agy read',
  ...data,
);

/** The `claude-only` arm of an answer to the usage mode, or a failure that says what it was instead. */
export function claudeOnlyOf(answer: AccountsRead): Extract<AccountsRead, { kind: 'claude-only' }> {
  if (answer.kind !== 'claude-only') {
    throw new Error(`the answer is not claude-only: ${answer.kind === 'unreadable' ? answer.why : answer.kind}`);
  }

  return answer;
}

/** The `read` arm of an answer, or a failure that says what it was instead. */
export function readOf(answer: AccountsRead): Extract<AccountsRead, { kind: 'read' }> {
  if (answer.kind !== 'read') {
    throw new Error(`the answer is not read: ${answer.kind === 'unreadable' ? answer.why : answer.kind}`);
  }

  return answer;
}

export type { AccountsMode };
