import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml, type PanelState } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import type { DataLocation } from '../dataDir';

/**
 * A click must post ONE message, however long the panel has been open.
 *
 * <p>The panel patches four live regions on a five-second tick, and the controls inside a patched
 * region have to be bound again because the markup they lived in was replaced. Binding them on
 * every tick instead — which is what the code did — adds a listener each time without removing the
 * one before it: `addEventListener` adds, it does not replace. After a minute a single press on
 * *Open notifications* posts twelve messages and opens twelve windows. (codex, the S5 code round;
 * the questions region three lines away had the identical defect and is fixed with it.)</p>
 *
 * <p><b>RUN, not read.</b> The first version of this file asserted statement adjacency in
 * `panelView.ts` and said in its own header that nothing here could run the panel's script. That
 * was wrong twice: `panelStorageScript.test.ts` already runs it, and `.agents/PROJECT.md` refuses
 * new behavioural assertions over page source text outright — *"A webview page is tested by RUNNING
 * it"*. A structural test could not see the defect it was written for: duplicate listeners on the
 * same node leave every statement exactly where it was. (CodeRabbit, on the pull request.)</p>
 */

const WHERE: DataLocation = {
  directory: '/srv/coai/windows',
  side: 'windows',
  ignoredSide: '',
  alsoWatched: [],
  refusal: '',
  notes: [],
  env: { COAI_DATA_DIR: '/srv/coai', COAI_DATA_SIDE: 'windows' },
  source: 'this side',
};

const state = (): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  agyModels: [],
  codexModels: [],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  perSide: false,
  questions: [],
  sessions: [],
  openSections: ['server'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  storage: WHERE,
});

/**
 * A control that keeps EVERY listener it was given, which is the whole point.
 *
 * <p>A fake that remembered only the last one would fire once however many times it was bound, and
 * the defect under test is precisely a second binding on the same node.</p>
 */
class Button {
  readonly dataset: Record<string, string>;
  private readonly presses: Array<() => void> = [];

  constructor(command: string, id: string) {
    this.dataset = { command, id };
  }

  addEventListener(kind: string, handler: () => void): void {
    if (kind === 'click') {
      this.presses.push(handler);
    }
  }

  click(): void {
    assert.ok(this.presses.length > 0, `nothing listens to ${this.dataset['command']}`);
    for (const press of [...this.presses]) {
      press();
    }
  }
}

/** One live region: assigning its innerHTML replaces the controls inside it, as a browser would. */
class Region {
  private html: string;
  private controls: Button[] = [];

  constructor(html: string) {
    this.html = html;
    this.controls = Region.controlsIn(html);
  }

  private static controlsIn(html: string): Button[] {
    return [...html.matchAll(/data-command="([a-zA-Z]+)"(?: data-id="([^"]*)")?/gu)]
      .map((found) => new Button(found[1] as string, found[2] ?? ''));
  }

  get innerHTML(): string {
    return this.html;
  }

  set innerHTML(next: string) {
    this.html = next;
    this.controls = Region.controlsIn(next);
  }

  querySelectorAll(selector: string): readonly Button[] {
    return selector.includes('data-command') ? this.controls : [];
  }

  only(): Button {
    assert.equal(this.controls.length, 1, 'this region should hold exactly one control');

    return this.controls[0] as Button;
  }
}

/** The last `<script>` the panel ships, cut out of its own html. */
function pageScript(html: string): string {
  const open = html.lastIndexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the panel has no script to run');

  return html.slice(start, end);
}

interface Running {
  readonly posted: unknown[];
  readonly regions: ReadonlyMap<string, Region>;
  tick(patch: Record<string, string>): void;
}

/** The panel's own script, running over its own regions. */
function run(): Running {
  const posted: unknown[] = [];
  const html = panelHtml(state(), 'test-nonce');
  const regions = new Map<string, Region>(
    ['live-questions', 'live-rounds', 'live-consultations', 'live-notifications']
      .map((id) => [id, new Region('')] as const),
  );
  let heard: ((event: { data: unknown }) => void) | undefined;

  const fakeDocument = {
    addEventListener: () => undefined,
    querySelectorAll: (): readonly Button[] => [],
    querySelector: (): null => null,
    getElementById: (id: string): Region | null => regions.get(id) ?? null,
    body: { style: { fontSize: '' } },
    documentElement: { style: { setProperty: () => undefined } },
  };

  // eslint-disable-next-line no-new-func -- the shipped script IS the thing under test.
  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout',
    pageScript(html));
  body(
    () => ({
      postMessage: (message: unknown) => { posted.push(message); },
      getState: () => undefined,
      setState: () => undefined,
    }),
    fakeDocument,
    {
      addEventListener: (kind: string, handler: (event: { data: unknown }) => void): void => {
        if (kind === 'message') {
          heard = handler;
        }
      },
    },
    (): number => 0,
    (): void => undefined,
  );
  assert.ok(heard !== undefined, 'the script never listened for a live message');

  return {
    posted,
    regions,
    tick: (patch) => (heard as (event: { data: unknown }) => void)({ data: { type: 'live', ...patch } }),
  };
}

const ONE_BUTTON = '<p><button type="button" data-command="showNotifications">Read them</button></p>';

test('a region that was replaced binds its new controls, and a press posts once', () => {
  const page = run();

  page.tick({ notifications: ONE_BUTTON });
  (page.regions.get('live-notifications') as Region).only().click();

  assert.deepEqual(
    page.posted,
    [{ type: 'command', command: 'showNotifications', id: '' }],
    'the button inside the patched markup reaches the host',
  );
});

test('a tick that changed NOTHING does not bind the same control a second time', () => {
  // The defect, and the reason a structural test could not see it: binding twice leaves every
  // statement exactly where it was, and the page looks identical. Twelve ticks a minute.
  const page = run();

  page.tick({ notifications: ONE_BUTTON });
  for (let again = 0; again < 5; again += 1) {
    page.tick({ notifications: ONE_BUTTON });
  }
  (page.regions.get('live-notifications') as Region).only().click();

  assert.equal(page.posted.length, 1, 'one press, one message — not one per tick since the paint');
});

test('the questions region three lines away behaves the same way', () => {
  // It had the identical defect and was fixed with it; asserting only the region this branch added
  // would leave the other half green through the same bug.
  const page = run();
  const answer = '<button data-command="answer" data-id="q1">Answer…</button>';

  page.tick({ questions: answer });
  page.tick({ questions: answer });
  page.tick({ questions: answer });
  (page.regions.get('live-questions') as Region).only().click();

  assert.deepEqual(page.posted, [{ type: 'command', command: 'answer', id: 'q1' }]);
});

test('every live region is patched, and one that changed is bound while the others are left alone', () => {
  const page = run();

  page.tick({
    questions: '<button data-command="answer" data-id="q2"></button>',
    rounds: '<i>a round</i>',
    consultations: '<i>a consultation</i>',
    notifications: ONE_BUTTON,
  });

  assert.equal((page.regions.get('live-rounds') as Region).innerHTML, '<i>a round</i>');
  assert.equal((page.regions.get('live-consultations') as Region).innerHTML, '<i>a consultation</i>');
  (page.regions.get('live-notifications') as Region).only().click();
  (page.regions.get('live-questions') as Region).only().click();

  assert.deepEqual(page.posted, [
    { type: 'command', command: 'showNotifications', id: '' },
    { type: 'command', command: 'answer', id: 'q2' },
  ]);
});
