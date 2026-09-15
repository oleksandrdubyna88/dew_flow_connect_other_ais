import assert from 'node:assert/strict';
import { test } from 'node:test';

import { panelHtml, type PanelState } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import type { DataLocation } from '../dataDir';

/**
 * The storage section's controls, RUN — not read.
 *
 * <p><b>This file exists because a reviewer quoted the rule at me and was right.</b>
 * `.agents/PROJECT.md`: "A webview page is tested by RUNNING it… A new behavioural assertion over
 * page source text is refused." The first version of these assertions matched
 * `data-command="changeDataDirectory"` in the rendered html, which is exactly the shape that cannot
 * see a control wired to the wrong branch — the string contains everything it was supposed to
 * contain. This repository has hit that twice.</p>
 *
 * <p>So the page's own script is cut out of the page it ships and executed against a synthetic
 * document, and what is asserted is what a press POSTS.</p>
 *
 * <p><b>And the controls come from the RENDERED page, not from a fixture</b> — which the first draft
 * of this file got wrong, in precisely the way the rule's last sentence warns about. It handed the
 * script a list of buttons it had invented, so deleting the button from `storageBlock` left both
 * tests green: the script dutifully bound a listener to a control that no longer existed anywhere.
 * Measured by deleting it. The section's own markup is now the source of the controls, so a deleted
 * button is a button this cannot find and cannot press.</p>
 */

const WHERE: DataLocation = {
  directory: '/srv/coai/windows',
  side: 'windows',
  ignoredSide: '',
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

/** Just enough of a button for the script's `dataset` reads and its click listener. */
class Button {
  readonly dataset: Record<string, string>;
  private press: (() => void) | undefined;

  constructor(command: string) {
    this.dataset = { command };
  }

  addEventListener(kind: string, handler: () => void): void {
    if (kind === 'click') {
      this.press = handler;
    }
  }

  click(): void {
    assert.ok(this.press !== undefined, `nothing listens to ${this.dataset['command']}`);
    this.press();
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

/**
 * The controls the STORAGE SECTION actually renders, as objects the page's script can bind to.
 *
 * <p>Read out of the section's own markup rather than declared here, which is what makes a deleted
 * button visible: there is then nothing to return, nothing to press, and the test says which command
 * it could not find.</p>
 */
function controlsInTheStorageSection(html: string): Map<string, Button> {
  const section = html.slice(html.indexOf('Where this window keeps its data'));
  const mine = section.slice(0, section.indexOf('</details>'));

  return new Map([...mine.matchAll(/data-command="([a-zA-Z]+)"/gu)]
    .map((found) => [found[1]!, new Button(found[1]!)] as const));
}

/** Run the panel's own script over the section's own controls, and collect what it posts. */
function run(): { posted: unknown[]; press(command: string): void } {
  const posted: unknown[] = [];
  const html = panelHtml(state(), 'test-nonce');
  const controls = controlsInTheStorageSection(html);
  const fakeDocument = {
    addEventListener: () => undefined,
    querySelectorAll: (selector: string): readonly Button[] =>
      (selector.includes('data-command') ? [...controls.values()] : []),
    querySelector: (): null => null,
    getElementById: (): null => null,
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
    { addEventListener: () => undefined },
    (): number => 0,
    (): void => undefined,
  );

  return {
    posted,
    press: (command) => {
      const control = controls.get(command);
      assert.ok(control !== undefined,
        `the storage section renders no ${command} control, so nothing can press it`);
      control.click();
    },
  };
}

test('pressing Change where your data lives asks the host to change it', () => {
  const page = run();

  page.press('changeDataDirectory');

  assert.deepEqual(page.posted, [{ type: 'command', command: 'changeDataDirectory', id: undefined }]);
});

test('and pressing Move what is here asks the host to move it', () => {
  const page = run();

  page.press('moveDataDirectory');

  assert.deepEqual(page.posted, [{ type: 'command', command: 'moveDataDirectory', id: undefined }]);
});
