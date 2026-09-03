import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CliStatus } from '../cliVersions';
import { panelHtml, PANEL_COMMANDS } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { Runtime } from '../models';
import { Vendor } from '../vendors';

/**
 * The update button says, by its colour, whether there is anything to update.
 *
 * <p>That is the question somebody actually has standing in front of this panel, and until now they
 * answered it by leaving. Green when the vendor publishes something newer, grey otherwise — and
 * grey is also the "could not tell" state, because a button that lights up when a fetch fails is
 * worse than one that never lights up.</p>
 */

function vendor(id: string, runtime = id as Runtime): Vendor {
  return {
    id,
    runtime,
    model: '',
    baseUrl: '',
    executablePath: '',
    enabled: true,
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  };
}

function html(vendors: readonly Vendor[], cliStatus: Record<string, CliStatus>): string {
  return panelHtml({
    settings: DEFAULTS,
    vendors,
    codexModels: [],
    localEngines: {},
    serverInstalled: false,
    serverVersion: '',
    latestServerVersion: '',
    questions: [],
    sessions: [],
    openSections: [],
    usage: [],
    usageWindow: 'week',
    cliStatus,
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce');
}

/** The row for one vendor, so a two-vendor panel cannot make another vendor's button count. */
function rowOf(page: string, id: string): string {
  const start = page.indexOf(`data-command="updateVendorCli" data-id="${id}"`);
  assert.notEqual(start, -1, `${id} has no update button`);

  return page.slice(page.lastIndexOf('<button', start), page.indexOf('</button>', start));
}

test('a newer published version turns the button green', () => {
  const page = html([vendor('codex')], { codex: { installed: '0.151.0', latest: '0.152.0' } });

  assert.match(rowOf(page, 'codex'), /class="run upd has-update"/);
});

test('the newest installed version leaves it grey', () => {
  // The real pair from this machine on the day this shipped: agy 1.1.23 installed, 1.1.23 published.
  const page = html([vendor('antigravity')], { antigravity: { installed: '1.1.23', latest: '1.1.23' } });

  assert.doesNotMatch(rowOf(page, 'antigravity'), /has-update/);
});

test('a version nobody could read is grey, never green', () => {
  for (const status of [
    { installed: '', latest: '2.0.0' },
    { installed: '2.0.0', latest: '' },
    { installed: '', latest: '' },
  ]) {
    const page = html([vendor('claude')], { claude: status });
    assert.doesNotMatch(rowOf(page, 'claude'), /has-update/, JSON.stringify(status));
  }
});

test('a vendor with no status at all still gets a button', () => {
  // The first paint, before any version has been read: the row must be complete, not half-drawn.
  const page = html([vendor('codex')], {});

  assert.doesNotMatch(rowOf(page, 'codex'), /has-update/);
});

test('the colour is never the only signal', () => {
  const stale = rowOf(html([vendor('codex')], { codex: { installed: '0.1.0', latest: '0.2.0' } }), 'codex');
  const fresh = rowOf(html([vendor('codex')], { codex: { installed: '0.2.0', latest: '0.2.0' } }), 'codex');

  // Both numbers reach a person in words: the aria-label for a screen reader, the tooltip for
  // everyone. A green pixel is the fast path, not the only one.
  assert.match(stale, /aria-label="Update the codex CLI to 0\.2\.0"/);
  assert.match(fresh, /aria-label="The codex CLI is up to date"/);
  assert.match(stale, /title="[^"]*0\.1\.0[^"]*0\.2\.0/);
});

test('every vendor in the panel gets its own button and its own state', () => {
  const page = html([vendor('codex'), vendor('antigravity')], {
    codex: { installed: '0.151.0', latest: '0.152.0' },
    antigravity: { installed: '1.1.23', latest: '1.1.23' },
  });

  assert.match(rowOf(page, 'codex'), /has-update/);
  assert.doesNotMatch(rowOf(page, 'antigravity'), /has-update/);
});

test('the command the button sends is one the provider must handle', () => {
  // PANEL_COMMANDS is what the provider switches on under a `never` guard, so a command in the
  // markup that is missing here is a dead button — which is exactly how the Update coai-mcp button
  // shipped doing nothing.
  assert.ok(PANEL_COMMANDS.includes('updateVendorCli'));
});

test('every collapsible header carries a tone, and no section is left grey', () => {
  const page = html([vendor('codex')], {});
  const sections = [...page.matchAll(/<details class="section sec-([a-z]+)"/g)].map((m) => m[1]!);

  assert.ok(sections.length >= 8, `only ${sections.length} sections found`);
  for (const id of sections) {
    assert.match(
      page,
      new RegExp(`\\.sec-${id}\\s*> summary \\{ color: var\\(--tone-`),
      `the ${id} header has no tone, so it renders grey beside coloured neighbours`,
    );
  }
});

test('each tone is a theme token with a fallback, never a bare hex', () => {
  // The rule the palette actually follows, borrowed from CredsForDevs: a `--vscode-*` token so a
  // theme that defines it wins, and a hex behind it so a theme that does not still gets the colour
  // that was meant. Not every tone is a CHART colour — `--tone-code` is a border — and asserting
  // that would be inventing a rule the code never had.
  const page = html([vendor('codex')], {});
  const tones = [...page.matchAll(/--tone-[a-z]+: ([^;]+);/g)].map((m) => m[1]!);

  assert.ok(tones.length >= 6, `only ${tones.length} tones defined`);
  for (const tone of tones) {
    assert.match(tone, /^var\(--vscode-[a-z-]+, #[0-9a-f]{6}\)$/, tone);
  }
});
