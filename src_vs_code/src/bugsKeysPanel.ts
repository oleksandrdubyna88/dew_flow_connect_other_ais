import { randomBytes } from 'node:crypto';

import * as vscode from 'vscode';

import { Admin, Answer, KeyRow, issue, keys, mayCarryAKey, revoke } from './bugsAdminApi';
import {
  Pending,
  Secrets,
  adminKey,
  beginIssuance,
  endIssuance,
  holdIssuance,
  issuanceAttempt,
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
 * veto any of them. The key is written to `SecretStorage` the instant the server answers and is
 * cleared only by a copy or by a discard the ISSUING server confirmed.</p>
 *
 * <p><b>And the window that remains is narrowed rather than denied.</b> The code round found it
 * three times: the server commits the key before this process hears anything, so a death in that
 * instant leaves a live key with no local record, and two commits cannot be made atomic from one
 * side. So an ATTEMPT is recorded before the request leaves. It cannot name the key — nothing here
 * ever can — but the next open says a key may exist and points at the newest row, which is what
 * story 2 ordered the listing for. Closing it completely needs a server-side idempotency record,
 * and that is a change to `coai-bugs` rather than to this file.</p>
 */
export class BugsKeysPanel {
  private panel: vscode.WebviewPanel | undefined;

  /** Where in the listing we are, as cursors already used. */
  private trail: Trail = START;

  /**
   * What the last action said.
   *
   * <p>Cleared only once it has actually been RENDERED. It used to be cleared at the end of every
   * draw, including a draw whose face could not show it — so the one sentence most worth keeping,
   * *a key may exist and must not be asked for again*, was thrown away precisely when the server
   * was unwell and that face appeared. (Three findings, two reviewers.)</p>
   */
  private said = '';

  /** The rows currently on screen, so a revoke can name the one it is about. */
  private rows: readonly KeyRow[] = [];

  /** The cursor the page on screen offered for the NEXT page, or empty at the end. */
  private nextCursor = '';

  /** Whether an action is in flight, so the page can disable what would queue another. */
  private busy = false;

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

  /**
   * Asks for the key and stores it, from the command as well as from the tab.
   *
   * <p>The redraw afterwards is a no-op when no panel is open, which is what makes it safe to share
   * ONE instance between the command and the section button. Two instances was the defect the code
   * round found: setting the key through the command left an open tab sitting on its rejected face
   * until somebody pressed Refresh, because the key went into one object and the webview belonged
   * to another. (Code round, codex.)</p>
   */
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
    await this.draw(START);
  }

  /** One action at a time, then a redraw from the server. */
  private queue(type: string, id: string): void {
    this.inFlight = this.inFlight
      .then(() => this.act(type, id))
      .catch(async (error_: unknown) => {
        this.busy = false;
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

  /**
   * What each message does.
   *
   * <p>A table rather than a chain of `if`s: seven branches in one method was over this
   * repository's complexity ceiling, and a list of equal alternatives is what a table is for.</p>
   */
  private async act(type: string, id: string): Promise<void> {
    const doing: Readonly<Record<string, () => Promise<void>>> = {
      setkey: () => this.askForKey(),
      refresh: () => this.draw(START),
      next: () => this.step('next'),
      back: () => this.step('back'),
      issue: () => this.issueOne(),
      revoke: () => this.revokeOne(id),
      copy: () => this.settlePending('copy'),
      discard: () => this.settlePending('discard'),
      dismiss: () => this.dismissOrphan(),
    };

    const what = doing[type];
    if (what === undefined) {
      // `ready`, and anything a future page posts that this build has never heard of.
      return;
    }

    this.busy = true;
    try {
      await what();
    } finally {
      // A failure must not leave every control disabled for ever; `queue` reports it and the next
      // draw is honest about what is there.
      this.busy = false;
    }
  }

  /**
   * One page forward or back.
   *
   * <p>The trail is committed only when the page it names actually ARRIVES — see
   * <see cref="users"/>. It used to be assigned before the draw, so a Next onto a page the server
   * then refused left the trail on a cursor belonging to a listing nobody had seen, and Back from
   * there walked a history that never happened. (Code round, gemini.)</p>
   */
  private async step(type: 'next' | 'back'): Promise<void> {
    const wanted = type === 'back'
      ? back(this.trail)
      : (this.nextCursor.length > 0 ? forward(this.trail, this.nextCursor) : this.trail);

    await this.draw(wanted);
  }

  /**
   * Issues one key: the attempt before the request, the key before anything is shown.
   *
   * <p><b>Refused while a pending key is held.</b> There is one slot, so a second issuance would
   * overwrite the first — leaving a live key whose only copy was in the record just destroyed. The
   * plan's rule is that a key is copied or discarded; this is that rule enforced rather than
   * hoped for. (Two reviewers.)</p>
   */
  private async issueOne(): Promise<void> {
    if (await pendingIssuance(this.secrets) !== undefined) {
      this.said = 'Copy or discard the key you already have before issuing another: there is one '
        + 'place to keep it, and a second issuance would overwrite the first.';
      await this.draw();

      return;
    }

    const server = this.server();
    const unsafe = mayCarryAKey(server);
    if (unsafe.length > 0) {
      this.said = unsafe;
      await this.draw();

      return;
    }

    const note = await vscode.window.showInputBox({
      title: 'What is this key for?',
      prompt: 'Our record of why it exists — "the tuesday workshop". Never the holder\'s name or address.',
      ignoreFocusOut: true,
    });
    if (note === undefined) {
      return;
    }

    // BEFORE the request leaves. Everything after this line can die and the next open still knows
    // that a key may exist.
    await beginIssuance(this.secrets, { server, note });
    const answer = await issue({ server, key: await adminKey(this.secrets) }, note);
    if (answer.kind !== 'ok') {
      await this.failedIssue(answer);

      return;
    }

    await holdIssuance(this.secrets, { ...answer.value, server });
    this.said = 'A key was issued. Copy it now — it cannot be read back.';
    await this.draw(START);
  }

  /**
   * An issuance that did not come back.
   *
   * <p>The attempt is FORGOTTEN when the server certainly did not act — a refusal, a rejected
   * credential, a rate limit, an address never sent to — and KEPT when it may have: a timeout, a
   * dead connection, an answer that could not be read. Keeping it is what makes the next open able
   * to say so.</p>
   */
  private async failedIssue(answer: Answer<unknown>): Promise<void> {
    const certainlyNot = answer.kind === 'refused' || answer.kind === 'rejected'
      || answer.kind === 'limited' || answer.kind === 'unsafe';
    if (certainlyNot) {
      await endIssuance(this.secrets);
    }

    this.said = afterFailedIssue(answer);
    await this.draw(START);
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
    // and a ratchet counts the call sites that bypass it — this one went red on the first run.
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

  /** A pending key is copied, or discarded — and discarding REVOKES it. */
  private async settlePending(type: 'copy' | 'discard'): Promise<void> {
    const pending = await pendingIssuance(this.secrets);
    if (pending === undefined) {
      await this.draw();

      return;
    }

    if (type === 'copy') {
      // The release is AFTER the write, so a clipboard that refuses keeps the key: the rejection
      // reaches `queue`'s catch and nothing is forgotten.
      await vscode.env.clipboard.writeText(pending.key);
      await releaseIssuance(this.secrets);
      this.said = 'The key is on the clipboard. It cannot be shown again.';
      await this.draw();

      return;
    }

    await this.discard(pending);
  }

  /**
   * Revokes a held key against its OWN issuer, and forgets it only on proof.
   *
   * <p>The issuer is carried on the record because the address is a setting and can be changed
   * while a key is held: a 404 from a server that never had it would otherwise be read as proof
   * that the key was gone, while it stayed alive on the one that issued it. (Code round, codex.)</p>
   */
  private async discard(pending: Pending): Promise<void> {
    const issuer = pending.server.length > 0 ? pending.server : this.server();
    const answer = await revoke({ server: issuer, key: await adminKey(this.secrets) }, pending.id);
    if (answer.kind === 'ok' || answer.kind === 'missing') {
      await releaseIssuance(this.secrets);
      this.said = 'The key was discarded and revoked, so nothing is left alive that nobody holds.';
      await this.draw();

      return;
    }

    // NOT "it is still active". A 401 says the credential was refused and says NOTHING about the
    // key, which another administrator may already have revoked; claiming a state the server
    // declined to report is the same mistake the rejected face exists to avoid. (Code round, codex.)
    this.said = `The key could not be revoked, so it is kept here until it can be: ${answer.why}`;
    await this.draw();
  }

  /** The person has checked the listing after an orphaned attempt; stop saying it. */
  private async dismissOrphan(): Promise<void> {
    await endIssuance(this.secrets);
    await this.draw();
  }

  private async admin(): Promise<Admin> {
    return { server: this.server(), key: await adminKey(this.secrets) };
  }

  /**
   * Draws, optionally moving to another page first.
   *
   * <p><c>wanted</c> is committed only when its page arrives, so a refused or unreachable answer
   * leaves the tab where it was rather than on a cursor nobody has seen.</p>
   */
  private async draw(wanted: Trail = this.trail): Promise<void> {
    if (this.panel === undefined) {
      return;
    }

    const users = await this.users(wanted);
    if (this.panel === undefined) {
      // Disposed while the server was being asked. Painting now is the `Webview is disposed` this
      // repository has been caught by once — and `said` is deliberately NOT cleared here, so
      // whatever the last action reported is still waiting when the tab is opened again.
      return;
    }

    this.panel.webview.html = usersPageHtml(users, nonce());
    // Cleared only now, and only because it has just been rendered: every face carries `said`, so
    // reaching this line means the sentence was shown.
    this.said = '';
  }

  /** What to draw: the pending key, an orphaned attempt, and whichever face the server earned. */
  private async users(wanted: Trail): Promise<Users> {
    const pending = await pendingIssuance(this.secrets);
    const attempt = pending === undefined ? await issuanceAttempt(this.secrets) : undefined;
    const held = await adminKey(this.secrets);
    const around = {
      ...(pending === undefined ? {} : { pending }),
      ...(attempt === undefined ? {} : { orphaned: attempt }),
      busy: this.busy,
    };

    if (held.length === 0) {
      this.rows = [];

      return { view: { kind: 'no-key', said: this.said }, ...around };
    }

    const answer = await keys({ server: this.server(), key: held }, here(wanted));
    if (answer.kind === 'ok') {
      // The page ARRIVED, so this is where we are now — and not a moment earlier.
      this.trail = wanted;
      this.rows = answer.value.items;
      this.nextCursor = answer.value.nextBefore ?? '';
    }

    return { view: faceOf(answer, this.trail, this.said, here(wanted).length > 0), ...around };
  }
}

/**
 * The ONE Users panel this extension has.
 *
 * <p>The command and the section button both reach it. They used to build one each, and the code
 * round found what that costs: setting the key through the command stored it in one object while
 * the webview belonged to another, so an open tab sat on its rejected face until somebody pressed
 * Refresh. One instance means the key and the window are the same object's business. (codex.)</p>
 */
let theOne: BugsKeysPanel | undefined;

/** The Users panel, built once. */
export function usersPanel(secrets: Secrets, server: () => string): BugsKeysPanel {
  theOne ??= new BugsKeysPanel(secrets, server);

  return theOne;
}

/** One nonce per paint: the CSP admits our one script and nothing else. */
function nonce(): string {
  return randomBytes(24).toString('base64url');
}
