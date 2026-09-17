import { KeyRow } from './bugsAdminApi';
import { Pending } from './bugsAdminKey';

/**
 * The Users tab: every contributor key, what it has sent, and the one button that ends it.
 *
 * <p>Markup and script as text, like every page here, so the script can be RUN by a test rather
 * than read — `.agents/PROJECT.md` refuses a new behavioural assertion over page source, and a list
 * with a destructive button is exactly where a control wired to the neighbouring row is the defect
 * that matters.</p>
 *
 * <p><b>What this page cannot show, it SAYS.</b> A key is displayed once, at issuance, and never
 * again — so a row has no key and no hash, because the wire has no field for either. Administrators
 * are not rows in `api_keys` at all: their credentials come from an environment variable and their
 * own uploads are counted against nothing, so they appear nowhere here. Leaving a reader to work
 * that out from an absence is how an empty list gets read as a broken tab.</p>
 *
 * <p><b>There are no page numbers.</b> The server pages by an opaque forward-only cursor, so
 * "page 3 of 10" cannot be computed and a Back button cannot be derived — the extension keeps the
 * cursors it has already used and pops one. `total` is rendered as a count of keys, which is what
 * it is, and never as a number of pages.</p>
 */

/**
 * Which face the tab is showing.
 *
 * <p><b>Every face carries `said`</b>, and that is a code-round finding rather than symmetry for
 * its own sake: the sentence explaining what an action came to was rendered only by the listing, so
 * a revoke that failed and then a listing that also failed told the person nothing at all about the
 * revoke. The most valuable sentence this tab produces — *a key MAY have been created, do not try
 * again, look at the newest row* — is exactly the one that arrives when the server is unwell, which
 * is precisely when the face is not the listing. (Three findings, two reviewers.)</p>
 */
export type View =
  /** No key has been set on this machine yet. */
  | { readonly kind: 'no-key'; readonly said: string }
  /**
   * The server would not accept the key.
   *
   * <p>ONE state for every 401, carrying the server's own sentence and NOT a diagnosis: an absent
   * `COAI_BUGS_ADMIN_KEYS`, a wrong key and a revoked one are deliberately indistinguishable, so a
   * tab that says "your key is invalid" sends somebody to re-enter a key that was always right.</p>
   */
  | { readonly kind: 'rejected'; readonly why: string; readonly said: string }
  /** The server could not be reached, which is not a credential problem and must not read as one. */
  | { readonly kind: 'unreachable'; readonly why: string; readonly said: string }
  /**
   * The server ANSWERED and refused — a 400, or a 404 for something that is not there.
   *
   * <p>Its own face because folding it into `unreachable` said *the server could not be reached*
   * about a server that had just replied, and offered a Try again that would repeat the same
   * refused request. A stale cursor is the ordinary way to arrive here, so the way out is to go
   * back to the newest page rather than to retry. (Code round, codex.)</p>
   */
  | { readonly kind: 'refused'; readonly why: string; readonly said: string }
  /**
   * The address itself is refused, before anything was sent.
   *
   * <p>Not a server problem and not a key problem: the key never left this machine.</p>
   */
  | { readonly kind: 'unsafe'; readonly why: string; readonly said: string }
  /** Rate-limited, with what the server asked us to wait. */
  | {
    readonly kind: 'limited';
    readonly why: string;
    readonly retryAfterSeconds: number;
    readonly said: string;
  }
  /** Keys, and whether another page may exist. */
  | {
    readonly kind: 'keys';
    readonly rows: readonly KeyRow[];
    readonly total: number;
    readonly hasNext: boolean;
    readonly hasBack: boolean;
    /** What the last action said, shown above the table. Empty when there is nothing to say. */
    readonly said: string;
    /**
     * Whether this page is the end of a walk rather than an empty table.
     *
     * <p>Following the cursor ends with one empty page, by the server's contract. Telling somebody
     * who has just paged through fifty keys that "no keys have been issued yet" is false, and it is
     * the sort of false that makes a person doubt the rest of the screen.</p>
     */
    readonly pagedPastTheEnd: boolean;
  };

/** Everything the page draws. */
export interface Users {
  readonly view: View;
  /** A key the server issued that nobody has confirmed holding. Survives a closed window. */
  readonly pending?: Pending;
  /**
   * An issuance that was asked for and never accounted for.
   *
   * <p>Written before the request left, so this is what a crash between the server's commit and
   * this machine's write leaves behind. It cannot name the key — nothing can — so it says a key may
   * exist and points at the newest row.</p>
   */
  readonly orphaned?: { readonly server: string; readonly note: string };
  /**
   * Whether something is in flight right now.
   *
   * <p>A request here may take ten seconds. Without this the panel sat on stale rows with every
   * control live, so pressing Issue twice queued two issuances — two live keys, one of them
   * unaccounted for, from one impatient person. (Two reviewers.)</p>
   */
  readonly busy?: boolean;
}

/**
 * The same page with its controls disabled or given back — or nothing when it already is.
 *
 * <p>Both ends of one defect. A request here may take ten seconds, and the page was painted only
 * when the action ENDED: nothing said anything was happening while it ran, so Issue pressed twice
 * queued two issuances — and the page it then painted was the one carrying `busy`, which disables
 * every control including Refresh. A tab nothing could be pressed on, for ever. So the panel
 * repaints what is already on screen at both moments, without asking the server again, and this
 * says when there is anything to repaint. (Round 1 found the silence, round 2 the dead tab.)</p>
 */
export function withControls(painted: Users | undefined, busy: boolean): Users | undefined {
  return painted === undefined || (painted.busy === true) === busy
    ? undefined
    : { ...painted, busy };
}

/** `yyyy-MM`, or the word for a key nobody has used. */
export function lastSeen(row: KeyRow): string {
  return row.lastSeenMonth === undefined || row.lastSeenMonth.length === 0
    ? 'never'
    : row.lastSeenMonth;
}

/** What a row is: in force, or ended and when. */
export function standing(row: KeyRow): string {
  return row.revokedUtc === undefined || row.revokedUtc.length === 0
    ? 'in force'
    : `revoked ${row.revokedUtc.slice(0, 10)}`;
}

/** Whether this row can still be revoked. */
export function live(row: KeyRow): boolean {
  return row.revokedUtc === undefined || row.revokedUtc.length === 0;
}

const escapes: Readonly<Record<string, string>> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
};

/** Everything from the server is somebody else's text, including a note an administrator typed. */
export function safe(text: string): string {
  return text.replace(/[&<>"']/gu, (c) => escapes[c] ?? c);
}

/** The whole page. */
export function usersPageHtml(users: Users, nonce: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  *, *::before, *::after { box-sizing: border-box; }
  :root { color-scheme: light dark; }
  body {
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
    color: var(--vscode-foreground); background: var(--vscode-editor-background);
    margin: 0; padding: 12px 16px;
  }
  h1 { font-size: 15px; margin: 0 0 4px; }
  .hint { opacity: .7; font-size: 12px; margin: 0 0 12px; }
  .bar { display: flex; gap: 8px; align-items: center; margin: 0 0 10px; flex-wrap: wrap; }
  button {
    background: var(--vscode-button-background); color: var(--vscode-button-foreground);
    border: none; border-radius: 2px; padding: 5px 12px; cursor: pointer;
    font-family: inherit; font-size: inherit;
  }
  button:disabled { opacity: .5; cursor: default; }
  button.quiet { background: none; color: var(--vscode-textLink-foreground); text-decoration: underline; }
  button.danger { background: var(--vscode-inputValidation-errorBorder); }
  table { width: 100%; border-collapse: collapse; }
  th { text-align: left; font-size: 11px; text-transform: uppercase; letter-spacing: .06em;
       opacity: .7; padding: 4px 8px; border-bottom: 1px solid var(--vscode-panel-border); }
  td { vertical-align: top; padding: 8px; border-bottom: 1px solid var(--vscode-panel-border); }
  td.id { font-family: var(--vscode-editor-font-family); font-size: 11px; opacity: .8; }
  .note { font-weight: 600; }
  .state { font-size: 11px; opacity: .6; }
  .revoked { opacity: .55; }
  .said { padding: 8px 10px; margin: 0 0 10px; border-left: 2px solid var(--vscode-textLink-foreground); }
  .trouble { border-left-color: var(--vscode-inputValidation-errorBorder); }
  .pending {
    border: 1px solid var(--vscode-inputValidation-warningBorder); border-radius: 3px;
    padding: 10px 12px; margin: 0 0 12px;
  }
  .pending code {
    display: block; margin: 6px 0; padding: 6px 8px; word-break: break-all;
    font-family: var(--vscode-editor-font-family); font-size: 11px;
    background: var(--vscode-textCodeBlock-background);
  }
  .empty { opacity: .7; padding: 24px 0; }
</style>
</head>
<body>
<h1>Who holds a key</h1>
<p class="hint">
  A key is shown ONCE, when it is issued, and cannot be read back afterwards &mdash; this list holds
  no key and no hash. Administrators are not listed here at all: their credentials come from the
  server's own environment, and what they upload is counted against no key.
</p>
${orphanBlock(users.orphaned)}
${pendingBlock(users.pending, users.busy === true)}
${saidBlock(users.view.said)}
${face(users.view, users.busy === true)}
<script nonce="${nonce}">
(function () {
  var vscode = acquireVsCodeApi();

  function post(type, extra) {
    var message = { type: type };
    if (extra) { for (var k in extra) { if (Object.prototype.hasOwnProperty.call(extra, k)) { message[k] = extra[k]; } } }
    vscode.postMessage(message);
  }

  document.addEventListener('click', function (event) {
    var target = event.target;

    // ABOVE anything that acts on the table, and it RETURNS. A control inside a row that fell
    // through would also trigger whatever the row does, which is the defect PROJECT.md records
    // and the one a source assertion cannot see. The id comes off the BUTTON, so a revoke can
    // only ever name the row it sits in.
    var revoking = target.closest ? target.closest('[data-revoke]') : null;
    if (revoking) {
      post('revoke', { id: revoking.getAttribute('data-revoke') });
      return;
    }

    if (target.id === 'issue') { post('issue'); return; }
    if (target.id === 'setkey') { post('setkey'); return; }
    if (target.id === 'refresh') { post('refresh'); return; }
    if (target.id === 'next') { post('next'); return; }
    if (target.id === 'back') { post('back'); return; }
    if (target.id === 'copy') { post('copy'); return; }
    if (target.id === 'discard') { post('discard'); return; }
    if (target.id === 'dismiss') { post('dismiss'); return; }
  });

  post('ready');
}());
</script>
</body>
</html>`;
}

/** The key nobody has confirmed holding, if one is waiting. */
function pendingBlock(pending: Pending | undefined, busy: boolean): string {
  if (pending === undefined) {
    return '';
  }

  return `<div class="pending" id="pending">
  <div><b>This key was issued and never copied.</b> It is active on the server, and this is the only
  place it still exists &mdash; it cannot be read back from the server again.</div>
  <code id="pendingkey">${safe(pending.key)}</code>
  <div class="bar">
    <button type="button" id="copy"${off(busy)}>Copy the key</button>
    <button type="button" class="danger" id="discard"${off(busy)}>Discard and revoke it</button>
  </div>
  <div class="state">Discarding revokes it on the server, so no key is left alive that nobody holds.</div>
</div>`;
}

/** `disabled` while something is in flight, so a second press cannot queue a second action. */
function off(busy: boolean): string {
  return busy ? ' disabled' : '';
}

/** What the last action said. Rendered above EVERY face, not only the listing. */
function saidBlock(said: string): string {
  return said.length === 0 ? '' : `<div class="said" id="said">${safe(said)}</div>`;
}

/**
 * An issuance that was asked for and never accounted for.
 *
 * <p>This is what a crash between the server's commit and this machine's write leaves behind. It
 * cannot show the key, because the server said it once and this process never heard it — so it says
 * what IS knowable: a key may exist, it would be the newest row, and nobody holds it.</p>
 */
function orphanBlock(orphaned: { server: string; note: string } | undefined): string {
  if (orphaned === undefined) {
    return '';
  }

  return `<div class="pending" id="orphaned">
  <div><b>A key may have been issued and never reached this machine.</b> The request was sent to
  ${safe(orphaned.server)}${orphaned.note.length > 0 ? ` for "${safe(orphaned.note)}"` : ''} and
  nothing came back — the server creates the key before it answers, so it may exist.</div>
  <div class="state">It cannot be shown: a key is returned once and this window never received it.
  If it is there it is the NEWEST row below, and revoking it is how you make sure nobody holds a key
  you do not.</div>
  <div class="bar"><button type="button" class="quiet" id="dismiss">I have checked the list</button></div>
</div>`;
}

/** Whichever face this view is. */
function face(view: View, busy: boolean): string {
  if (view.kind === 'no-key') {
    return `<div class="empty" id="nokey">
  No admin key is set on this machine. It is kept in the editor's secret storage, never in settings,
  because settings sync between machines.
  <div class="bar"><button type="button" id="setkey"${off(busy)}>Set the bugs admin key</button></div>
</div>`;
  }

  if (view.kind === 'unsafe') {
    // NOT a key problem and not a server problem: nothing was sent. Saying so matters because the
    // other two faces both invite an action that would be useless here.
    return `<div class="empty" id="unsafe">
  <b>The key was not sent anywhere.</b>
  <div class="said trouble">${safe(view.why)}</div>
  Change the ingest server in the Bugz section, then open this again.
</div>`;
  }

  if (view.kind === 'refused') {
    return `<div class="empty" id="refused">
  <b>The server refused that request.</b>
  <div class="said trouble">${safe(view.why)}</div>
  It answered, so this is not a connection problem. If you were paging, start again from the newest
  keys.
  <div class="bar"><button type="button" id="refresh"${off(busy)}>Back to the newest</button></div>
</div>`;
  }

  if (view.kind === 'rejected') {
    // NO DIAGNOSIS. The server answers a wrong key, a revoked key and a server with no
    // administrators configured identically, on purpose, so that this endpoint cannot be asked
    // whether administration is enabled. Naming one of the three would be a guess presented as a
    // fact, and the wrong guess sends somebody to re-enter a key that was always correct.
    return `<div class="empty" id="rejected">
  <b>The server did not accept this key.</b>
  <div class="said trouble">${safe(view.why)}</div>
  This server answers the same way whether the key is wrong, has been revoked, or no administrators
  are configured on it at all &mdash; so this tab cannot tell you which. Check the key held here, and
  check the server's own startup log, which is the only place the difference is visible.
  <div class="bar"><button type="button" id="setkey"${off(busy)}>Set the bugs admin key</button></div>
</div>`;
  }

  if (view.kind === 'unreachable') {
    return `<div class="empty" id="unreachable">
  <b>The server could not be reached.</b> This is not a problem with your key.
  <div class="said trouble">${safe(view.why)}</div>
  <div class="bar"><button type="button" id="refresh"${off(busy)}>Try again</button></div>
</div>`;
  }

  if (view.kind === 'limited') {
    return `<div class="empty" id="limited">
  <b>Too many requests.</b> The server limits each administrator, and this one has reached it.
  <div class="said trouble">${safe(view.why)}</div>
  Try again in about ${view.retryAfterSeconds} s. Nothing is retried automatically, because a
  silent retry spends the next window as well.
  <div class="bar"><button type="button" id="refresh"${off(busy)}>Try again</button></div>
</div>`;
  }

  return table(view, busy);
}

/** The listing, with its controls. */
function table(view: Extract<View, { kind: 'keys' }>, busy: boolean): string {
  const { rows, total, hasNext, hasBack } = view;
  const body = rows.length === 0
    ? emptyTable(view.pagedPastTheEnd)
    : `<table>
<thead><tr><th>Note</th><th>Id</th><th>Last used</th><th>Sent</th><th>Waiting</th><th></th></tr></thead>
<tbody>
${rows.map((one) => row(one, busy)).join('\n')}
</tbody>
</table>`;

  return `<div class="bar">
  <button type="button" id="issue"${off(busy)}>Issue a key</button>
  <button type="button" class="quiet" id="refresh"${off(busy)}>Refresh</button>
  <button type="button" class="quiet" id="back"${hasBack && !busy ? '' : ' disabled'}>Back</button>
  <button type="button" class="quiet" id="next"${hasNext && !busy ? '' : ' disabled'}>Next</button>
  <span class="hint" id="total">${total} ${total === 1 ? 'key' : 'keys'}</span>
  ${busy ? '<span class="hint" id="busy">Working\u2026</span>' : ''}
</div>
${body}`;
}

/**
 * An empty table says WHICH empty it is.
 *
 * <p>Following the cursor ends with one empty page, by the server's own contract — so "no keys have
 * been issued yet" is what an administrator sees after paging through fifty of them. The two are
 * different facts and only one of them is ever true.</p>
 */
function emptyTable(pagedPastTheEnd: boolean): string {
  return pagedPastTheEnd
    ? '<div class="empty" id="pastend">That is the end of the list. Go Back for the previous page.</div>'
    : '<div class="empty" id="nokeys">No keys have been issued yet.</div>';
}

/** One key. The revoke button carries its OWN id, so it cannot act on a neighbour. */
function row(key: KeyRow, busy = false): string {
  const ended = !live(key);

  return `<tr class="${ended ? 'revoked' : ''}">
  <td><div class="note">${safe(key.note.length > 0 ? key.note : '(no note)')}</div>
      <div class="state">${safe(standing(key))}</div></td>
  <td class="id">${safe(key.id)}</td>
  <td>${safe(lastSeen(key))}</td>
  <td>${key.sent}</td>
  <td>${key.waiting}</td>
  <td>${ended ? '' : `<button type="button" class="danger" data-revoke="${safe(key.id)}"${off(busy)}>Revoke</button>`}</td>
</tr>`;
}
