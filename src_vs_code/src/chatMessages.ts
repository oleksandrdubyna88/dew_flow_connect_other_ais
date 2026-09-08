/**
 * What a message from the chat page MEANS, decided without a host.
 *
 * <p>It exists because of one finding on the plan round: every test in epic 2 exercised the pure
 * registry and the pure page, while the only module that maps a webview message to an action was the
 * one no unit test can reach. A wrong message type would then ship with every test green — a person's
 * send quietly ignored, or delivered somewhere else. So the DECISION moved here and the `vscode` half
 * kept only the wiring, which is the part a test genuinely cannot reach.</p>
 *
 * <p>The page and the host ship in one `.vsix` today, but a webview retained across a reload can be
 * older than the extension that talks to it. An unknown message is therefore ignored rather than
 * thrown on: the older half must not break because the newer one learned a word.</p>
 */

/** Anything the page might post. Every field optional, because the page is not to be trusted. */
export interface PageMessage {
  readonly type?: unknown;
  readonly command?: unknown;
  readonly text?: unknown;
  readonly id?: unknown;
  readonly delta?: unknown;
  readonly message?: unknown;
}

export type ChatCommand =
  | { readonly kind: 'send'; readonly text: string }
  | { readonly kind: 'pick'; readonly id: string }
  | { readonly kind: 'zoom'; readonly delta: number }
  | { readonly kind: 'restart' }
  | { readonly kind: 'useLocal' }
  | { readonly kind: 'pageError'; readonly message: string }
  | { readonly kind: 'ignore' };

const IGNORE: ChatCommand = { kind: 'ignore' };

/** A string, or nothing at all — the page's values arrive over a bridge and are not typed there. */
function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** The zoom control posts its own shape, and it is not a `command`. */
function zoomOf(message: PageMessage): ChatCommand {
  const delta = typeof message.delta === 'number' && Number.isFinite(message.delta) ? message.delta : 0;

  return { kind: 'zoom', delta };
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
    case 'restart':
      return { kind: 'restart' };
    case 'useLocal':
      return { kind: 'useLocal' };
    default:
      return IGNORE;
  }
}
