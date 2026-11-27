import type { CopyDecision } from './copyText';
import type { ManagerWord } from './remoteAccountsReply';
import { INSTALL_JQ } from './remoteAccountsRows';
import { blockOf } from './remoteAccountsTable';
import type { Block, TableModel } from './remoteAccountsTable';

/**
 * What a message from the accounts tab MEANS (E2.S3, contract C5), decided without a host. Pure: a page message and the model that the host holds
 * go in; a closed command, or nothing, comes out. The page is not trusted: every name is checked against a closed list and every argument against
 * the host's own model, and what is not known is dropped.
 *
 * <p><b>A message names, it does not carry.</b> A host is named, never described; a command to copy is never part of a message: the page says
 * `copy-install <place>` and the host looks the command up in `INSTALL_JQ` by that place's manager word. The two messages that WIDEN what is read,
 * `allow` and `read-again`, change nothing by themselves: the host raises a question for each, and only the person's answer there does. The
 * *Group by* switch, the *Sort by* choice and the headers post nothing, so no kind for them exists here.</p>
 */

export const COPY_DONE = 'The command is copied.';
export const COPY_FAILED = 'The command could not be copied.';

/** Every name the page may post, closed. */
export const PAGE_WORDS = ['ready', 'refresh', 'stop', 'start', 'allow', 'withdraw', 'save-now', 'refuse', 'read-again', 'copy-install'] as const;

export type AccountsCommand =
  | { readonly kind: 'ready' | 'refresh' | 'stop' | 'start' | 'allow' | 'withdraw' | 'save-now' }
  | { readonly kind: 'refuse'; readonly host: string }
  | { readonly kind: 'read-again'; readonly host: string }
  | { readonly kind: 'copy-install'; readonly decision: CopyDecision };

type Plain = 'ready' | 'refresh' | 'stop' | 'start' | 'allow' | 'withdraw' | 'save-now';

const PLAIN: ReadonlySet<string> = new Set<Plain>(['ready', 'refresh', 'stop', 'start', 'allow', 'withdraw', 'save-now']);

const recordOf = (message: unknown): Readonly<Record<string, unknown>> =>
  (typeof message === 'object' && message !== null && !Array.isArray(message) ? (message as Readonly<Record<string, unknown>>) : {});

/** The block a message names, when it names a string that is a place of the model. */
const blockNamed = (model: TableModel | undefined, name: unknown): Block | undefined =>
  (model === undefined || typeof name !== 'string' ? undefined : blockOf(model, name));

const wordOf = (block: Block | undefined): ManagerWord | null => (block?.head.kind === 'jq-missing' ? block.head.manager : null);

/** The fixed command of a place whose `jq` is missing, by its manager word: nothing for any other place and for a host with no manager. */
const commandOf = (block: Block | undefined): string | undefined => {
  const word = wordOf(block);

  return word === null ? undefined : INSTALL_JQ[word];
};

function installOf(block: Block | undefined): CopyDecision | undefined {
  const text = commandOf(block);

  return text === undefined ? undefined : { kind: 'copy', text, done: COPY_DONE, failed: COPY_FAILED };
}

type Named = (said: Readonly<Record<string, unknown>>, model: TableModel | undefined) => AccountsCommand | undefined;

/** The names that carry an argument, each with the rule its argument must pass. */
const NAMED: Readonly<Record<string, Named>> = {
  'copy-install': (said, model) => {
    const decision = installOf(blockNamed(model, said['place']));

    return decision === undefined ? undefined : { kind: 'copy-install', decision };
  },
  refuse: (said, model) => {
    const block = blockNamed(model, said['host']);

    return block?.canRefuse === true ? { kind: 'refuse', host: block.host } : undefined;
  },
  'read-again': (said, model) => {
    const block = blockNamed(model, said['host']);

    return block?.canReadAgain === true ? { kind: 'read-again', host: block.host } : undefined;
  },
};

/** The command a page message asks for, or `undefined` for anything this host does not know. */
export function accountsCommandOf(message: unknown, model: TableModel | undefined): AccountsCommand | undefined {
  const said = recordOf(message);
  const type = said['type'];
  if (typeof type !== 'string') {
    return undefined;
  }
  if (PLAIN.has(type)) {
    return { kind: type as Plain };
  }

  return Object.hasOwn(NAMED, type) ? NAMED[type](said, model) : undefined;
}
