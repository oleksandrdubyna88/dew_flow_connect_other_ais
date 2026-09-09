/**
 * What a message from the chat page MEANS, decided without a host.
 *
 * <p>It exists because of one finding on the plan round: every test in epic 2 exercised the pure
 * registry and the pure page, while the only module that maps a webview message to an action was the
 * one no unit test can reach. A wrong message type would then ship with every test green — a person's
 * send quietly ignored, or delivered somewhere else. So the DECISION moved here and the `vscode` half
 * kept only the wiring, which is the part a test genuinely cannot reach.</p>
 *
 * <p><b>An unknown message is IGNORED, and that is a deliberate exception to a written rule.</b>
 * `coding-style.md` says an unknown name fails naming the legal values rather than falling back
 * silently, and that rule is right where it was written: a SETTING somebody typed, where a silent
 * fallback hides a typo until the behaviour surprises them. This is not that boundary. A webview
 * retained across a reload can be older — or newer — than the extension talking to it, and a host
 * that refused a word it did not know would break the half that had done nothing wrong. The values
 * are enumerated in `ChatCommand` and every one of them is tested, including the unknown case; what
 * is silent is the ignoring, not the vocabulary. (codex, the code round, asked for this reconciled
 * explicitly rather than left as two rules pointing opposite ways.)</p>
 */

/** Anything the page might post. Every field optional, because the page is not to be trusted. */
export interface PageMessage {
  readonly type?: unknown;
  readonly command?: unknown;
  readonly text?: unknown;
  readonly id?: unknown;
  readonly delta?: unknown;
  readonly message?: unknown;
  readonly turn?: unknown;
  readonly url?: unknown;
  readonly file?: unknown;
  readonly line?: unknown;
  readonly index?: unknown;
}

export type ChatCommand =
  | { readonly kind: 'send'; readonly text: string }
  | { readonly kind: 'pick'; readonly id: string }
  /**
   * End the turn numbered `turn`, and no other. Always 1 or more.
   *
   * <p>The number is what stops a LATE stop from ending the turn after the one it was pressed for.
   * The page disables its own control, but a keybinding does not go through it and a bridge message
   * can land a tick after the answer did — by which time the next question may already be in
   * flight.</p>
   *
   * <p><b>There is no wildcard, deliberately.</b> The first draft let a missing number mean "whichever
   * is running", for a page too old to send one. Two vendors pointed out that this hands back the
   * exact defect the number exists to prevent — and there is no such page: `stop` and its turn number
   * are being added in the same release, so a page that can ask for a stop can always say which one.
   * A stop that names no turn is therefore not a stop, and is ignored like any other message this
   * host does not understand.</p>
   */
  | { readonly kind: 'stop'; readonly turn: number }
  | { readonly kind: 'zoom'; readonly delta: number }
  | { readonly kind: 'restart' }
  | { readonly kind: 'useLocal' }
  | { readonly kind: 'pageError'; readonly message: string }
  /**
   * A link in an ANSWER, which is text another vendor's model wrote.
   *
   * <p>The page emits no `href` at all — it names what it wants and the host decides — so this is
   * the boundary, and it validates rather than trusts. Only `http` and `https` become an `openLink`;
   * every other scheme, `javascript:` and `data:` and `file:` alike, is not a link this product
   * opens on anybody's behalf.</p>
   */
  | { readonly kind: 'openLink'; readonly url: string }
  /**
   * A file reference in an answer, to be opened in the editor at a line.
   *
   * <p>Relative and confined, checked HERE and again against the real workspace root before anything
   * is opened: a model writing `.git/config` or `../../.ssh/id_rsa` is not a hypothetical, and
   * neither end of this is allowed to be the only one that says no.</p>
   */
  | { readonly kind: 'openFile'; readonly path: string; readonly line: number }
  | { readonly kind: 'copyAnswer'; readonly index: number }
  | { readonly kind: 'ignore' };

const IGNORE: ChatCommand = { kind: 'ignore' };

/** A string, or nothing at all — the page's values arrive over a bridge and are not typed there. */
function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * The zoom control posts its own shape, and it is not a `command`.
 *
 * <p>The delta is CLAMPED to one step, not merely checked for finiteness. `applyZoomDelta` clamps
 * the resulting scale, so a huge value could not have broken the layout — but it would have jumped
 * the size to a bound in one message, from a surface the host does not control. The control itself
 * only ever sends ±1, so anything else is not a zoom. (gemini, the code round.)</p>
 */
function zoomOf(message: PageMessage): ChatCommand {
  if (typeof message.delta !== 'number' || !Number.isFinite(message.delta)) {
    return { kind: 'zoom', delta: 0 };
  }

  return { kind: 'zoom', delta: Math.max(-1, Math.min(1, Math.trunc(message.delta))) };
}

/**
 * Which turn a stop names, or `0` for "this named no turn at all".
 *
 * <p>`0` is not a value a caller may act on — `chatCommandOf` turns it into `ignore`. It exists only
 * so this function has something to return for input that is not a turn: a missing field, a string,
 * a negative, a fraction, `NaN`, an unsafe integer. All of those mean the message cannot be obeyed,
 * and refusing them here is the boundary rule — a value from a surface the host does not control is
 * validated before it reaches anything that acts on it. (Three vendors, the code round: an earlier
 * version coerced every one of these into a wildcard that stopped whatever happened to be running.)</p>
 */
function turnOf(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) {
    return 0;
  }

  return value;
}

/**
 * A path this product will open on the strength of a model having written it.
 *
 * <p>Relative, no traversal, no drive letter, no scheme, no leading slash, and no backslash — a
 * Windows separator is not how this repository names a file and accepting one is a second grammar to
 * get wrong. The renderer applies the same shape before it emits the anchor; this is the boundary
 * that matters, because the page is a surface and anything can post to it.</p>
 *
 * <p>It is deliberately NOT enough on its own: the caller resolves it against the real workspace
 * root and checks the result is still inside. A string can be confined-looking and still escape
 * through a symlink, which only the filesystem knows about.</p>
 */
export function isConfinedRelativePath(value: string): boolean {
  if (value.length === 0 || value.length > 400) {
    return false;
  }
  if (value.startsWith('/') || value.startsWith('\\') || value.includes('\\') || value.includes('..')) {
    return false;
  }
  if (/^[A-Za-z]:/.test(value) || /^[a-z][a-z0-9+.-]*:/i.test(value)) {
    return false;
  }

  return /^[\w./-]+$/.test(value);
}

/**
 * Is `absolute` really inside `root`?
 *
 * <p><b>Not a prefix.</b> With a root of `/w/app`, the path `/w/app-secret/config.json` starts with
 * it and belongs to a different project — somebody else's, quite possibly. Containment is a boundary
 * at a separator, so the root is compared with one appended, and equality with the root itself is
 * allowed separately.</p>
 *
 * <p>Case-SENSITIVE, deliberately. Folding case would be a guess about the filesystem underneath,
 * and the guess is wrong on one of the two this product runs on. A path that differs only in case is
 * refused, which costs a person nothing — the reference came from a model, and it can be written
 * again — where the opposite mistake costs them a file they never meant to open.</p>
 */
export function isInside(root: string, absolute: string): boolean {
  if (root.length === 0) {
    return false;
  }
  const bounded = root.endsWith('/') ? root : `${root}/`;

  return absolute === root || absolute === bounded.slice(0, -1) || absolute.startsWith(bounded);
}

/** The line a file reference names, or 0 for "the top" — never negative, never a fraction. */
function lineOf(value: unknown): number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : 0;
}

/**
 * One message, read.
 *
 * <p>An empty `send` is `ignore` rather than a send of nothing: the page already refuses to post one,
 * and a host that would have sent it anyway is a host that spends a vendor turn on a stray Enter.</p>
 */
export function chatCommandOf(message: PageMessage | undefined): ChatCommand {
  if (message === undefined || message === null) {
    return IGNORE;
  }
  if (message.type === 'zoom') {
    return zoomOf(message);
  }
  if (message.type === 'pageError') {
    const said = text(message.message);

    return said.length === 0 ? IGNORE : { kind: 'pageError', message: said };
  }
  if (message.type !== 'command') {
    return IGNORE;
  }

  switch (message.command) {
    case 'send': {
      const said = text(message.text).trim();

      return said.length === 0 ? IGNORE : { kind: 'send', text: said };
    }
    case 'pick': {
      const id = text(message.id);

      return id.length === 0 ? IGNORE : { kind: 'pick', id };
    }
    case 'stop': {
      const turn = turnOf(message.turn);

      return turn === 0 ? IGNORE : { kind: 'stop', turn };
    }
    case 'openLink': {
      const url = text(message.url);
      if (url.length > 0) {
        return /^https?:\/\/[^\s]+$/i.test(url) ? { kind: 'openLink', url } : IGNORE;
      }
      const path = text(message.file);

      return isConfinedRelativePath(path)
        ? { kind: 'openFile', path, line: lineOf(message.line) }
        : IGNORE;
    }
    case 'copyAnswer': {
      const index = message.index;

      return typeof index === 'number' && Number.isSafeInteger(index) && index >= 0
        ? { kind: 'copyAnswer', index }
        : IGNORE;
    }
    case 'restart':
      return { kind: 'restart' };
    case 'useLocal':
      return { kind: 'useLocal' };
    default:
      return IGNORE;
  }
}
