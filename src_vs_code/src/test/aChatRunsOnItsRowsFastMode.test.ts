import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { chatFastSettingsFile } from '../chatFastSettings';
import { CLAUDE_ARGS, claudeAdapter } from '../claudeAdapter';
import { chatLaunchFor } from '../cliChatLaunch';
import { codexAdapter } from '../codexAdapter';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';

/**
 * A chat runs on its row's fast mode (todo/PLAN_fast_mode.md, Story C), by the rule a review launch follows: codex is told
 * `-c service_tier=default|fast`, claude a one-key `--settings` file — the extension's own pair, under the chat's own
 * folder, never coai-mcp's. Judged by the CONVERSATION's model, which is what the CLI is told, not the row's default.
 */

const row = (extra: Record<string, unknown>): Vendor =>
  vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id: 'chat-a', uses: ['chat'], enabled: true, ...extra }])[0]!;

const tiers = (argv: readonly string[]): readonly string[] =>
  argv.flatMap((one, at) => (one === '-c' && (argv[at + 1] ?? '').startsWith('service_tier=') ? [argv[at + 1]!] : []));

test('a codex chat is told its row\'s tier — Off by default, On, or nothing As the CLI is set', () => {
  const argv = (extra: Record<string, unknown>) => codexAdapter.argv(chatLaunchFor(row({ runtime: 'codex', ...extra }), '', 'gpt-6.1-sol', 'text'));

  assert.deepEqual(tiers(argv({})), ['service_tier=default']);
  assert.deepEqual(tiers(argv({ fast: 'on' })), ['service_tier=fast']);
  assert.deepEqual(tiers(argv({ fast: 'cli' })), []);
  assert.deepEqual(tiers(argv({ fast: 'on', baseUrl: 'https://or.example/v1' })), [], 'a codex row on another endpoint has no codex tier');
  const args = argv({ fast: 'on' });
  assert.ok(args.indexOf('service_tier=fast') < args.indexOf('-'), 'the tier goes before the stdin positional');
});

test('a claude chat on an Opus model is told its state through the chat\'s own one-key file; another model, nothing', () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-chat-fast-'));
  try {
    const file = (on: boolean) => chatFastSettingsFile(home, on);
    const argv = (model: string, extra: Record<string, unknown>) =>
      claudeAdapter.argv(chatLaunchFor(row({ runtime: 'claude', model: 'opus', ...extra }), '', model, 'text', file));
    const settingsOf = (args: readonly string[]) => readFileSync(args[args.indexOf('--settings') + 1]!, 'utf8');

    assert.equal(settingsOf(argv('claude-opus-5-5', { fast: 'on' })), '{"fastMode":true}');
    assert.equal(settingsOf(argv('claude-opus-5-5', {})), '{"fastMode":false}', 'Off is the default');
    assert.deepEqual(argv('claude-sonnet-5', { fast: 'on' }), [...CLAUDE_ARGS, '--model', 'claude-sonnet-5'], 'the CONVERSATION\'s model decides, and Sonnet has no tier');
    assert.ok(!argv('claude-opus-5-5', { fast: 'cli' }).includes('--settings'));
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test('the chat\'s file is written once, and only rewritten when it says something else', () => {
  const home = mkdtempSync(join(tmpdir(), 'coai-chat-fast-'));
  try {
    const path = chatFastSettingsFile(home, true);
    const written = statSync(path).mtimeMs;
    assert.equal(chatFastSettingsFile(home, true), path);
    assert.equal(statSync(path).mtimeMs, written, 'an unchanged file is left alone');
    assert.notEqual(chatFastSettingsFile(home, false), path, 'Off and On are two files');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});
