import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { Escalation } from './escalations';
import { LogRow, questionsHtml, roundsLogHtml } from './roundsLog';

/**
 * The rounds log page: one webview panel per window, reused while open.
 *
 * <p>The provider only ever PUSHES data. Sorting, filtering, searching and expanding a row are the
 * page's own state, and nothing about them comes back here — which is deliberate, and the lesson
 * of the sidebar's disclosures: a page state that round-trips through the extension host is a page
 * state that can be re-applied by a patch and fire its own event again.</p>
 *
 * <p>A push happens only when the rows or the questions actually changed. The watcher ticks every
 * five seconds whether or not anything did, and a page re-rendering its table for nothing would
 * drop the scroll position of somebody reading it.</p>
 */
export class RoundsLogPanel {
  private panel: vscode.WebviewPanel | undefined;
  private lastPayload = '';

  constructor(private readonly onAnswer: (id: string) => Promise<void>) {}

  /** Whether anybody is looking. When nobody is, the tick reads nothing for this page. */
  get isOpen(): boolean {
    return this.panel !== undefined;
  }

  show(rows: readonly LogRow[], questions: readonly Escalation[]): void {
    if (this.panel !== undefined) {
      this.panel.reveal();
      this.update(rows, questions, true);
      return;
    }
    const panel = vscode.window.createWebviewPanel(
      'coaiRoundsLog',
      'ConnectOtherAIs — Review rounds',
      vscode.ViewColumn.Active,
      // Kept alive while hidden behind another tab: the sort, the filters and the search text are
      // page state, and a page that is torn down when it is not visible loses them every time.
      { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] },
    );
    this.panel = panel;
    panel.webview.html = roundsLogHtml(rows, questions, crypto.randomBytes(16).toString('hex'));
    this.lastPayload = payloadOf(rows, questions);
    panel.webview.onDidReceiveMessage((message: { type?: string; command?: string; id?: string }) => {
      if (message.type === 'command' && message.command === 'answer' && typeof message.id === 'string') {
        void this.onAnswer(message.id);
      }
    });
    panel.onDidDispose(() => {
      this.panel = undefined;
      this.lastPayload = '';
    });
  }

  /** Pushes new rows, unless they are the rows the page already has. */
  update(rows: readonly LogRow[], questions: readonly Escalation[], force = false): void {
    if (this.panel === undefined) {
      return;
    }
    const payload = payloadOf(rows, questions);
    if (!force && payload === this.lastPayload) {
      return;
    }
    this.lastPayload = payload;
    void this.panel.webview.postMessage({ type: 'rows', rows, questions: questionsHtml(questions) });
  }
}

/**
 * What "changed" means: the serialised rows and questions.
 *
 * <p>A running round's `seconds` advances every tick by construction, so while something runs the
 * page is pushed every five seconds — which is right, that is the number somebody is watching.
 * When nothing runs, the payload is stable and nothing is sent.</p>
 */
function payloadOf(rows: readonly LogRow[], questions: readonly Escalation[]): string {
  return JSON.stringify([rows, questions]);
}
