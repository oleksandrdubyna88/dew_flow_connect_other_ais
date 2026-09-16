import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { PANEL_COMMANDS } from '../panelView';
import { logCommandOf } from '../roundsLogMessages';
import { roundsLogHtml, usageTabHtml } from '../roundsLog';
import type { ChatTurnRecord } from '../chatUsage';
import type { UsageEntry } from '../usage';

/**
 * The ✕ on a chat row, from the markup to the message the host receives.
 *
 * <p><b>This file exists because the markup was right and the page dropped the field.</b> The chat
 * row's button carries `data-model` — a chat row is a vendor AND a model, and one id cannot name the
 * pair — but the page's delegated `[data-command]` handler posted only `id`. Every assertion over
 * the rendered HTML was green; the button forgot nothing, because the half that says WHICH model
 * never left the page. That is the defect the gate's "run the page" findings were pointing at, and
 * it is why the middle test below EXECUTES the page's own script rather than reading it.</p>
 *
 * <p>The script is run the way the webview runs it: the listener the page registers on `document` is
 * captured and then called with an event whose target answers `closest` the way a real one would.
 * Nothing here stubs the function under test.</p>
 */


function todayAt(hours: number): string {
  const day = new Date();
  day.setHours(hours, 0, 0, 0);

  return day.toISOString();
}

const REVIEWER: UsageEntry = {
  utc: todayAt(9), provider: 'codex', model: 'gpt-5.6-sol', role: 'Architecture',
  stage: 'CodeReview', seconds: 23, tokensIn: 1_000_000, tokensOut: 200_000, costUsd: null, outcome: 'ok',
};

const TURN: ChatTurnRecord = {
  utc: todayAt(12), provider: 'codex', model: 'gpt-5.4', vendor: 'codex',
  tokensIn: 2_000_000, tokensOut: 1_000_000, costUsd: null, seconds: 4,
  outcome: 'answered', conversation: 'c1', title: 'a tab',
};

const PRICES = { 'gpt-5.4': { inPerMillion: 1, outPerMillion: 10, source: 'openrouter' as const } };

const CHAT = { turns: [TURN], doors: [] };

/** The spending tab, with both ledgers on it. */
function spendingTab(chat: { turns: readonly ChatTurnRecord[]; doors: readonly never[] } = CHAT): string {
  return usageTabHtml([REVIEWER], 'day', [], PRICES, [], 'me', chat);
}

/** The page's own click listener, captured out of its script exactly as the webview would run it. */
function pageClickListener(): (event: { target: unknown }) => void {
  const html = roundsLogHtml([], [], 'n0nce');
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const body = script.slice(script.indexOf('>') + 1);

  let click: ((event: { target: unknown }) => void) | undefined;
  const element = (): Record<string, unknown> => ({
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    addEventListener() {}, getAttribute: () => null, setAttribute() {}, querySelectorAll: () => [],
  });
  const document_ = {
    addEventListener(kind: string, fn: (event: { target: unknown }) => void) {
      if (kind === 'click') {
        click = fn;
      }
    },
    getElementById: () => element(),
    querySelectorAll: () => [],
    querySelector: () => element(),
    body: element(),
  };
  const posted: unknown[] = [];

  new Function('document', 'window', 'acquireVsCodeApi', body)(
    document_,
    { addEventListener() {}, matchMedia: () => ({ matches: false, addEventListener() {} }) },
    () => ({ postMessage: (message: unknown) => posted.push(message), getState: () => undefined, setState() {} }),
  );

  assert.ok(click, 'the page registers no click listener, so this test is driving nothing');
  (click as { posted?: unknown[] }).posted = posted;

  return click;
}

test('pressing the ✕ on a chat row sends the model as well as the vendor', () => {
  const click = pageClickListener();
  const posted = (click as unknown as { posted: unknown[] }).posted;

  const button = {
    getAttribute: (name: string) =>
      ({ 'data-command': 'forgetChat', 'data-id': 'codex', 'data-model': 'gpt-5.4' })[name] ?? null,
  };
  const before = posted.length; // the page posts `ready` when it loads; this is about the CLICK.
  click({ target: { closest: (selector: string) => (selector === '[data-command]' ? button : null) } });

  assert.deepEqual(
    posted.slice(before),
    [{ type: 'command', command: 'forgetChat', id: 'codex', model: 'gpt-5.4' }],
    'the press did not send the vendor and the model as one message',
  );
});

test('the message a chat ✕ sends decodes to the pair the host must forget', () => {
  const command = logCommandOf({ type: 'command', command: 'forgetChat', id: 'codex', model: 'gpt-5.4' });

  assert.deepEqual(command, { kind: 'forgetChat', provider: 'codex', model: 'gpt-5.4' });
});

test('a reviewer ✕ still decodes to the reviewer command, untouched', () => {
  // Both halves: this change must not have been bought by rerouting the control beside it.
  assert.deepEqual(
    logCommandOf({ type: 'command', command: 'forgetUsage', id: 'codex' }),
    { kind: 'forget', provider: 'codex' },
  );
});

test('forgetChat is a command the provider must handle', () => {
  // PANEL_COMMANDS is the closed vocabulary; the provider switches over it with a `never` check, so
  // a command missing a case is a compile error rather than a dead button.
  assert.ok(PANEL_COMMANDS.includes('forgetChat'), 'the chat ✕ posts a command nothing declares');
  assert.ok(PANEL_COMMANDS.includes('forgetUsage'), 'the reviewer ✕ lost its command');
});

test('a chat row carries the control, and the reviewers half still carries its own', () => {
  const page = spendingTab();

  // Both halves, so this cannot pass by having moved the reviewer button into the chat card.
  assert.match(page, /data-command="forgetChat" data-id="codex" data-model="gpt-5\.4"/u,
    'the chat row has no control, or it does not name the pair');
  assert.match(page, /data-command="forgetUsage" data-id="codex"/u,
    'the reviewers half lost the control it has had since this tab shipped');
});

test('the row that has nothing chosen has nothing to forget', () => {
  // A chat line can record no vendor at all — a real state, grouped as one rather than invented
  // into a row. There is no pair to forget, and a control that cannot act is worse than none.
  const page = spendingTab({ turns: [{ ...TURN, provider: '', model: '', vendor: '' }], doors: [] });

  assert.match(page, /nothing chosen/u, 'the fixture no longer produces the unnamed row');
  assert.doesNotMatch(page, /data-command="forgetChat"/u, 'the unnamed row was given a control it cannot use');
});

test('a model id that would break the attribute is escaped, not trusted', () => {
  // A model id is written by a vendor. One carrying a quote would close `data-model` early and let
  // the rest of it forge another attribute — so the button would forget a pair nobody pointed at.
  // (codex, the plan round.)
  const hostile = 'gpt-5" data-model="other';
  const page = spendingTab({ turns: [{ ...TURN, model: hostile }], doors: [] });

  assert.ok(!page.includes(`data-model="${hostile}"`), 'the model id reached the attribute unescaped');
  assert.match(page, /data-model="gpt-5&quot; data-model=&quot;other"/u, 'the model id was not escaped the way the rest of the page is');
});

test('the ledgers reach the page through the filter, and the marks default to none', () => {
  // A source assertion, and the reason is the standing one: `panelProvider.ts` needs a VS Code host
  // this suite has none of, so the wiring between a correct filter and the page cannot be executed
  // here. It matters because every test above passes with the filter perfect and the assembly site
  // never calling it — the gate said so, and it was right. The idiom is
  // `customEndpointIsOneFlow.test.ts`'s.
  const host = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8');

  // Both ledgers, not one: filtering turns and leaving doors would hide the money and keep the count.
  assert.match(host, /turns: rememberedChat\(await this\.chatLines\(\), marks, vendorOf\)/u,
    'the turns ledger does not go through the forget filter');
  assert.match(host, /doors: rememberedChat\(await this\.doorLines\(\), marks, vendorOf\)/u,
    'the doors ledger does not go through the forget filter');

  // A fresh install has no map at all; reading it must answer "nothing forgotten", not undefined.
  assert.match(host, /get<Record<string, string>>\('coai\.chatUsageForgottenBefore'\) \?\? \{\}/u,
    'a machine that has never forgotten anything would read undefined here');

  // And the reviewers' own watermark is untouched — two ledgers, two maps.
  assert.match(host, /get<Record<string, string>>\('coai\.usageForgottenBefore'\) \?\? \{\}/u,
    'the reviewers watermark was changed by a chat feature');
});
