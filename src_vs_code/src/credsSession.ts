/**
 * Which terminal reads a Claude session (E2.S1): the one rule for the name `Creds: <host> / <session>`.
 *
 * <p>The creds command names a terminal after the VM it opened and the tmux session it attached to. That name is the only
 * statement the extension has about where a Claude session runs, so it is read by ONE pattern, here, and nothing wider than
 * the pattern is ever a host or a session: both go to `ssh` and to a remote shell in a later story.</p>
 *
 * <p>No imports, so every other module may read it.</p>
 */

/** What a terminal's name says about a remote session. */
export type CredsName =
  /** A creds terminal with a legal host and tmux session. */
  | { readonly kind: 'session'; readonly host: string; readonly session: string }
  /** Not a creds terminal at all (`bash`, `Creds run: x`, `Creds VPN: x`, `creds: h / s`). */
  | { readonly kind: 'other' }
  /** It starts like a creds terminal and is not one; the refusal names the part that failed. */
  | { readonly kind: 'malformed'; readonly refusal: string };

/** The prefix that makes a name a creds terminal's, byte for byte (the case matters, and so does the space). */
const PREFIX = 'Creds: ';

/** The separator between the host and the session. */
const SEPARATOR = ' / ';

/**
 * A leading `-` is refused by the first class of each part, and that is the guard against an ssh or tmux option
 * injected through a terminal's name (`Creds: -oProxyCommand=x / s`). No space, quote, `$`, `;` or other shell character
 * is in any class either. A `.` is legal in a host and not in a session, because tmux changes a `.` in a session name.
 */
const HOST_CLASS = '[A-Za-z0-9][A-Za-z0-9._-]*';
const SESSION_CLASS = '[A-Za-z0-9][A-Za-z0-9_-]*';
const HOST = new RegExp(`^${HOST_CLASS}$`, 'u');

/** The whole name: the plan's pattern, built from the same two classes the refusal tests the parts with. */
const NAME = new RegExp(`^${PREFIX}(${HOST_CLASS})${SEPARATOR}(${SESSION_CLASS})$`, 'u');

/** The longest part a refusal quotes, in characters. */
const QUOTED_MAX = 80;

const SHAPE_SENTENCE = 'This terminal’s name is not `Creds: <host> / <session>`, so its Claude session cannot be read. Only the text you copied can be sent.';

/** A part as the refusal quotes it: as it is, cut at {@link QUOTED_MAX} characters with `…` after a cut. */
function quoted(part: string): string {
  const characters = [...part];

  return characters.length > QUOTED_MAX ? `${characters.slice(0, QUOTED_MAX).join('')}…` : part;
}

/** The refusal of a name that starts like a creds name: the shape, the host or the session — the first that fails. */
function refusalOf(rest: string): string {
  const parts = rest.split(SEPARATOR);
  if (parts.length !== 2) {
    return SHAPE_SENTENCE;
  }
  const [host, session] = parts;
  if (!HOST.test(host)) {
    return `The host “${quoted(host)}” in this terminal’s name is not a name ssh can be given safely (letters, digits, \`.\`, \`_\` and \`-\`, not first), so its Claude session is not read.`;
  }

  return `The tmux session “${quoted(session)}” in this terminal’s name cannot be read: only letters, digits, \`_\` and \`-\` are allowed (tmux changes a \`.\`).`;
}

/** What a terminal's full name says: a session to read, another kind of terminal, or a creds name that cannot be read. */
export function credsSession(name: string): CredsName {
  const matched = NAME.exec(name);
  if (matched !== null) {
    return { kind: 'session', host: matched[1], session: matched[2] };
  }

  return name.startsWith(PREFIX) ? { kind: 'malformed', refusal: refusalOf(name.slice(PREFIX.length)) } : { kind: 'other' };
}

/**
 * Whether the LOCAL home may be walked for a conversation of this source: never for a terminal, whose Claude session is on
 * another machine, and otherwise only when the conversation reads a session at all. The ONE seam both walks ask.
 */
export const walksLocalHome = (source: { readonly kind: string }, fromSession: boolean): boolean =>
  source.kind !== 'terminal' && fromSession;

/** What the Asked press says for a terminal conversation: it is not looked for on this computer. */
export const NO_LOCAL_SESSION = 'This chat came from a terminal: its Claude session is not on this computer, so it is not looked for here.';
