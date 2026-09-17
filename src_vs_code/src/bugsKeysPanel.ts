import { randomBytes } from 'node:crypto';

import * as vscode from 'vscode';

import { Admin, KeyRow, issue, keys, revoke } from './bugsAdminApi';
import {
  Secrets,
  adminKey,
  holdIssuance,
  pendingIssuance,
  releaseIssuance,
  setAdminKey,
} from './bugsAdminKey';
import {
  START,
  Trail,
  afterFailedIssue,
  afterRevoke,
  back,
  confirmRevoke,
  faceOf,
  forward,
  here,
} from './bugsKeysFlow';
import { Users, usersPageHtml } from './bugsKeysPage';
import { notify, notifyAndAsk } from './notify';

/**
 * The window the Users tab lives in.
 *
 * <p>A panel beside `BugzReviewPanel`, on its precedent and in its shape: the page posts what a
 * person pressed, the extension does it, and the page is then redrawn from what the SERVER says.
 * Nothing here paints an outcome it has not been told.</p>
 *
 * <p><b>The issuance guarantee does not live in this panel, and cannot.</b> A webview is disposed by
 * the editor's tab control, by closing the window and by an extension-host reload, and it cannot
 * veto any of them — so "the panel will not dismiss until the key is copied" is a promise a panel is
 * unable to keep. The key is written to `SecretStorage` the instant the server answers and is
 * cleared only by a copy or by a discard that REVOKES it; whatever becomes of this window, the next
 * open finds it. (Three plan reviewers, independently.)</p>
 */
export class BugsKeysPanel {
  private panel: vscode.WebviewPanel | undefined;

  /** Where in the listing we are, as cursors already used. */
  private trail: Trail = START;

  /** What the last action said, shown once above the table. */
  private said = '';

  /** The rows currently on screen, so a revoke can name the one it is about. */
  private rows: readonly KeyRow[] = [];

  /**
   * The cursor the page on screen offered for the NEXT page, or empty at the end.
   *
   * <p>Kept from the draw rather than fetched again when Next is pressed. Asking twice was a wasted
   * request and a race with itself: the second answer can differ from the one the person is looking
   * at, so Next would step onto a cursor that belongs to a listing they never saw.</p>
   */
  private nextCursor = '';

  /** Posts and redraws in order; two actions in flight would race the redraw between them. */
  private inFlight: Promise<void> = Promise.resolve();

  constructor(
    private readonly secrets: Secrets,
    private readonly server: () => string,
  ) {}

  get isOpen(): boolean {
    return this.panel !== undefined;
  }

  /** Opens it, or brings back the one already open and refreshes it. */
  async show(): Promise<void> {
    if (this.panel === undefined) {
      this.panel = vscode.window.createWebviewPanel(
        'coai.bugsKeys',
        'Who holds a key',
        vscode.ViewColumn.Active,
        { enableScripts: true, retainContextWhenHidden: true, enableFindWidget: true },
      );
      this.panel.onDidDispose(() => {
        this.panel = undefined;
      });
      this.panel.webview.onDidReceiveMessage((m: { type?: string; id?: string }) => {
        this.queue(m.type ?? '', m.id ?? '');
      });
    }

    await this.draw();
    this.panel?.reveal(vscode.ViewColumn.Active);
  }

  /** Asks for the key and stores it, from the command as well as from the tab. */
  async askForKey(): Promise<void> {
    const typed = await vscode.window.showInputBox({
      title: 'The bugs admin key',
      prompt: 'Kept in the editor\'s secret storage on this machine only — never in settings, which sync.',
      password: true,
      ignoreFocusOut: true,
    });
    if (typed === undefined) {
      return;
    }

    await setAdminKey(this.secrets, typed);
    this.trail = START;
    await this.draw();
  }

  /** One action at a time, then a redraw from the server. */
  private queue(type: string, id: string): void {
    this.inFlight = this.inFlight
      .then(() => this.act(type, id))
      .catch(async (error_: unknown) => {
        await notify({
          as: 'error',
          class: 'failure',
          source: 'bugsKeys',
          code: 'bugs-keys-action-failed',
          title: `The Users tab could not finish that: ${String(error_)}`,
          detail: String(error_),
        });
      });
  }

  private async act(type: string, id: string): Promise<void> {
    if (type === 'ready') {
      return;
    }

    if (type === 'setkey') {
      await this.askForKey();

      return;
    }

    if (type === 'refresh') {
      await this.draw();

      return;
    }

    if (type === 'next' || type === 'back') {
      await this.step(type);

      return;
    }

    if (type === 'issue') {
      await this.issueOne();

      return;
    }

    if (type === 'revoke') {
      await this.revokeOne(id);

      return;
    }

    if (type === 'copy' || type === 'discard') {
      await this.settlePending(type);
    }
  }

  /** Next uses the cursor the last page handed over; Back pops one already used. */
  private async step(type: 'next' | 'back'): Promise<void> {
    if (type === 'back') {
      this.trail = back(this.trail);
      await this.draw();

      return;
    }

    if (this.nextCursor.length > 0) {
      this.trail = forward(this.trail, this.nextCursor);
    }

    await this.draw();
  }

  /**
   * Issues one key and holds it before anything can be shown.
   *
   * <p>The write to `SecretStorage` happens between the server's answer and the redraw, which is the
   * window that loses a key. It is never retried: the server commits before it replies.</p>
   */
  private async issueOne(): Promise<void> {
    const note = await vscode.window.showInputBox({
      title: 'What is this key for?',
      prompt: 'Our record of why it exists — "the tuesday workshop". Never the holder\'s name or address.',
      ignoreFocusOut: true,
    });
    if (note === undefined) {
      return;
    }

    const answer = await issue(await this.admin(), note);
    if (answer.kind !== 'ok') {
      this.said = afterFailedIssue(answer);
      this.trail = START;
      await this.draw();

      return;
    }

    await holdIssuance(this.secrets, answer.value);
    this.said = 'A key was issued. Copy it now — it cannot be read back.';
    this.trail = START;
    await this.draw();
  }

  /** Revoke asks first, naming the note and the month rather than the id. */
  private async revokeOne(id: string): Promise<void> {
    const row = this.rows.find((candidate) => candidate.id === id);
    if (row === undefined) {
      // The row is not on screen any more, so the listing is stale rather than the click wrong.
      await this.draw();

      return;
    }

    // THROUGH THE FUNNEL, like every other thing this extension says. `notify.ts` is the one door
    // and a ratchet counts the call sites that bypass it — this one went red on the first run,
    // which is exactly what that counter is for. It also means the question and the answer are both
    // recorded, and a destructive action somebody was asked about is worth having on the record.
    const pressed = await notifyAndAsk({
      as: 'warning',
      class: 'confirmation',
      source: 'bugsKeys',
      code: 'revoke-a-contributor-key',
      subject: row.id,
      modal: true,
      title: confirmRevoke(row),
      action: 'Revoke it',
    });
    if (pressed !== 'Revoke it') {
      return;
    }

    this.said = afterRevoke(await revoke(await this.admin(), id), row.note);
    await this.draw();
  }

  /**
   * A pending key is copied, or discarded — and discarding REVOKES it.
   *
   * <p>Forgetting it instead would leave a live key nobody holds, which is the whole defect the
   * copy-before-dismiss rule was written against.</p>
   */
  private async settlePending(type: 'copy' | 'discard'): Promise<void> {
    const pending = await pendingIssuance(this.secrets);
    if (pending === undefined) {
      await this.draw();

      return;
    }

    if (type === 'copy') {
      await vscode.env.clipboard.writeText(pending.key);
      await releaseIssuance(this.secrets);
      this.said = 'The key is on the clipboard. It cannot be shown again.';
      await this.draw();

      return;
    }

    const answer = await revoke(await this.admin(), pending.id);
    if (answer.kind === 'ok' || answer.kind === 'missing') {
      // Gone from the server, so it is safe to forget here. Anything else keeps it: a key still
      // alive that this extension has dropped is exactly what must not happen.
      await releaseIssuance(this.secrets);
      this.said = 'The key was discarded and revoked, so nothing is left alive that nobody holds.';
    } else {
      this.said = `It is still active: ${answer.why} — it is kept here until it can be revoked.`;
    }

    await this.draw();
  }

  private async admin(): Promise<Admin> {
    return { server: this.server(), key: await adminKey(this.secrets) };
  }

  private async draw(): Promise<void> {
    if (this.panel === undefined) {
      return;
    }

    const users = await this.users();
    if (this.panel === undefined) {
      // Disposed while the server was being asked. Painting now is the `Webview is disposed` this
      // repository has been caught by once already.
      return;
    }

    this.panel.webview.html = usersPageHtml(users, nonce());
    this.said = '';
  }

  /** What to draw: the pending key if there is one, and whichever face the server earned. */
  private async users(): Promise<Users> {
    const pending = await pendingIssuance(this.secrets);
    const held = await adminKey(this.secrets);
    if (held.length === 0) {
      this.rows = [];

      return pending === undefined ? { view: { kind: 'no-key' } } : { view: { kind: 'no-key' }, pending };
    }

    const answer = await keys({ server: this.server(), key: held }, here(this.trail));
    this.rows = answer.kind === 'ok' ? answer.value.items : [];
    this.nextCursor = answer.kind === 'ok' ? answer.value.nextBefore ?? '' : '';
    const view = faceOf(answer, this.trail, this.said);

    return pending === undefined ? { view } : { view, pending };
  }
}

/** One nonce per paint: the CSP admits our one script and nothing else. */
function nonce(): string {
  return randomBytes(24).toString('base64url');
}
