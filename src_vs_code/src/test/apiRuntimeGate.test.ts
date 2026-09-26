import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import {
  API_DIALECTS,
  API_RUNTIME_SINCE,
  DEFAULT_API_DIALECT,
  apiRuntimeOnServer,
  apiRuntimeSkewNote,
  dialectChoices,
} from '../apiRuntime';
import { CHAT_RUNTIMES } from '../cliChatLaunch';
import { CONSULTING_RUNTIMES } from '../consultSettings';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { serverSettingsJson } from '../serverSettingsFile';
import { ServerSettingsSync } from '../serverSettingsSync';
import { DEFAULTS, envBlock, settingMessageFrom, settingWrite } from '../settingsShape';
import { DEFAULT_VENDORS, Vendor, vendorsEnv, vendorsFrom } from '../vendors';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * The `api` runtime's version gate and its dialect (PLAN_feature_review.md §4.13, S1.2 part iv).
 *
 * <p><b>The seam is the settings FILE, not the panel.</b> An older `coai-mcp` reads the file and turns a
 * runtime it does not know into codex WITH the base URL — a Grok review through the Codex CLI against
 * xAI's endpoint, under the row's own name. So a row against an older installed server is not only
 * disabled in the card: it is kept out of what the writer emits, while every other row still crosses.
 * The installed version therefore has to reach the writer, which it never did before this story.</p>
 */

const GROK: Vendor = {
  id: 'grok', runtime: 'api', model: 'grok-4', enabled: true, plan: true, code: true,
  baseUrl: 'https://api.x.ai/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0, dialect: 'openai',
};
const OLDER = '0.35.0';
const KNOWN = (version: string) => ({ kind: 'known' as const, version, remembered: false, updateOffered: false });

const repo = (...parts: string[]): string => join(__dirname, '..', '..', '..', ...parts);

function rowsOf(json: string): readonly { id: string; runtime: string; dialect?: string }[] {
  return JSON.parse(json) as { id: string; runtime: string; dialect?: string }[];
}

// ---------------------------------------------------------------- the one list, on both sides
//
// The runtime NAMES are not compared against `ReviewerRuntime.cs` here, deliberately: a test that
// parses another program's source goes quiet rather than red (`NothingReadsAnotherProgramsSourceTests`
// refuses it). The dialect list below IS held to a file neither half owns; the runtime set is held on
// the C# side by `VendorRuntimeSurvivesParsingTests` and here by `runtimeSurvives.test.ts`.

test('api is neither a chat partner nor a consultant — the two pickers derive from lists that exclude it', () => {
  assert.ok(!(CHAT_RUNTIMES as readonly string[]).includes('api'));
  assert.ok(!(CONSULTING_RUNTIMES as readonly string[]).includes('api'));
});

test('the dialect names mirror shared/api-dialects.json, and only the hosted ones are offered', () => {
  const shared = JSON.parse(readFileSync(repo('shared', 'api-dialects.json'), 'utf8')) as { dialects: Record<string, unknown> };

  assert.deepEqual([...API_DIALECTS], Object.keys(shared.dialects), 'a row added on one side is unknown to the other');
  assert.deepEqual([...dialectChoices()], ['openai'], 'the local body is the local reviewer’s; a hosted row never gets it');
  assert.equal(DEFAULT_API_DIALECT, 'openai');
});

test('a saved dialect survives the round trip, lower-cased, and an absent one stays absent', () => {
  const [named] = vendorsFrom([{ id: 'grok', runtime: 'api', model: 'm', dialect: ' OpenAI ' }]);
  const [unnamed] = vendorsFrom([{ id: 'grok', runtime: 'api', model: 'm' }]);

  assert.equal(named.dialect, 'openai');
  assert.ok(!('dialect' in (unnamed as object)), 'coai.vendors is JSON a person reads; an empty dialect on every row is noise');
});

// ---------------------------------------------------------------- the file

test('the api row crosses to a server that knows the runtime, with its dialect — and to an unknown one', () => {
  for (const version of [API_RUNTIME_SINCE, '1.0.0', '']) {
    const grok = rowsOf(vendorsEnv([...DEFAULT_VENDORS, GROK], version)).find((row) => row.id === 'grok');

    assert.ok(grok !== undefined, `the api row must reach a server at ${version || 'an unknown version'}`);
    assert.equal(grok.runtime, 'api');
    assert.equal(grok.dialect, 'openai');
  }
});

test('the api row is kept OUT of the file of an older server, and every other row still crosses', () => {
  const rows = rowsOf(vendorsEnv([...DEFAULT_VENDORS, GROK], OLDER));

  assert.deepEqual(rows.map((row) => row.id), ['codex', 'antigravity'],
    'an older server would run the api row as codex against its endpoint; the rest must not be lost with it');
});

test('envBlock and the settings file thread the installed version down to the vendor rows', () => {
  const env = envBlock(DEFAULTS, [...DEFAULT_VENDORS, GROK], OLDER);
  assert.ok(env['COAI_VENDORS'] !== undefined, 'a list that differs from the defaults is still emitted');
  assert.ok(!env['COAI_VENDORS'].includes('grok'));
  assert.ok(env['COAI_VENDORS'].includes('codex'));

  const written = JSON.parse(serverSettingsJson(DEFAULTS, [...DEFAULT_VENDORS, GROK], '0.53.1', OLDER)) as Record<string, string>;
  assert.ok(!written['COAI_VENDORS']!.includes('grok'));
  const current = JSON.parse(serverSettingsJson(DEFAULTS, [...DEFAULT_VENDORS, GROK], '0.53.1', API_RUNTIME_SINCE)) as Record<string, string>;
  assert.ok(current['COAI_VENDORS']!.includes('grok'));
});

test('the sync asks for the installed version at EVERY write, so an update lets the row through', async () => {
  let version = OLDER;
  const written: string[] = [];
  const sync = new ServerSettingsSync(
    () => ({ settings: DEFAULTS, vendors: [...DEFAULT_VENDORS, GROK] }),
    async (json: string) => {
      written.push(json);
    },
    '',
    undefined,
    undefined,
    undefined,
    () => version,
  );

  await sync.sync();
  version = API_RUNTIME_SINCE;
  await sync.sync();

  assert.equal(written.length, 2, 'the second sync is a change: the row that was held back now crosses');
  assert.ok(!written[0]!.includes('grok'));
  assert.ok(written[1]!.includes('grok'));
});

// ---------------------------------------------------------------- the sentence

test('the note names the row and the release, and stays silent when there is nothing to say', () => {
  assert.match(apiRuntimeSkewNote(OLDER, [GROK]), /grok/u);
  assert.ok(apiRuntimeSkewNote(OLDER, [GROK]).includes(API_RUNTIME_SINCE), 'the note names the release');
  assert.equal(apiRuntimeSkewNote('', [GROK]), '', 'an unknown server is not called old');
  assert.equal(apiRuntimeSkewNote(API_RUNTIME_SINCE, [GROK]), '');
  assert.equal(apiRuntimeSkewNote(OLDER, DEFAULT_VENDORS), '', 'no api row, nothing to warn about');
  assert.equal(apiRuntimeSkewNote(OLDER, [{ ...GROK, enabled: false }]), '', 'a switched-off row does not cross anyway');
  assert.equal(apiRuntimeOnServer(''), true);
  assert.equal(apiRuntimeOnServer(OLDER), false);
  // mcp-v0.36.0 was RELEASED on 2026-09-25 without the runtime (a sibling epic's feature), so it is
  // an old server for this purpose — the constant first said 0.36.0 and would have trusted it.
  assert.equal(apiRuntimeOnServer('0.36.0'), false, 'mcp 0.36.0 shipped without runtime "api"');
  assert.equal(apiRuntimeOnServer('0.37.1'), true);
});

// ---------------------------------------------------------------- the card, RUN

test('the api card offers a dialect picker, and changing it writes coai.vendors for that row', () => {
  const page = runPanel(panelState('reviewers', { vendors: [...DEFAULT_VENDORS, GROK], server: KNOWN(API_RUNTIME_SINCE) }));
  const picker = page.controls.filter((one) => one.dataset['setting'] === 'dialect');
  assert.equal(picker.length, 1, 'one picker, on the api row and nowhere else');
  assert.equal(picker[0]!.dataset['vendor'], 'grok');
  assert.equal(picker[0]!.disabled, false);

  picker[0]!.value = 'openai';
  picker[0]!.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(page))), { kind: 'vendor', key: 'dialect', vendor: 'grok', value: 'openai', control: 'select' });
});

test('against an older server every control of the api card is switched off and the card says why; the others are not', () => {
  const page = runPanel(panelState('reviewers', { vendors: [...DEFAULT_VENDORS, GROK], server: KNOWN(OLDER) }));
  const grok = page.controls.filter((one) => one.dataset['vendor'] === 'grok');
  const codex = page.controls.filter((one) => one.dataset['vendor'] === 'codex');

  assert.ok(grok.length >= 4, 'the api card has its enabled box, model, endpoint and dialect');
  assert.ok(grok.every((one) => one.disabled), `every api control is off: ${grok.filter((one) => !one.disabled).map((one) => one.dataset['setting']).join(', ')} still live`);
  assert.ok(codex.some((one) => !one.disabled), 'a codex row is untouched by the api gate');
  assert.match(page.html, /does not know the api runtime/u);
});

test('against a server that knows the runtime, or none the panel can version, the api card is live', () => {
  for (const server of [KNOWN(API_RUNTIME_SINCE), panelState('reviewers').server]) {
    const page = runPanel(panelState('reviewers', { vendors: [...DEFAULT_VENDORS, GROK], server }));
    const grok = page.controls.filter((one) => one.dataset['vendor'] === 'grok');

    assert.ok(grok.length >= 4);
    assert.ok(grok.every((one) => !one.disabled), server.kind);
    assert.doesNotMatch(page.html, /does not know the api runtime/u);
  }
});

// ---------------------------------------------------------------- the help, in five languages

test('every language’s choose-reviewers article names the API preset', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'choose-reviewers');
  assert.ok(article !== undefined, 'the reviewers article has been renamed, and this test is now asserting nothing');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    assert.equal(fallback, false, `the ${language} article is missing`);
    assert.ok(body.setup.includes('API (OpenAI-compatible)'), `the ${language} article does not name the API preset — stale rather than missing`);
  }
});
