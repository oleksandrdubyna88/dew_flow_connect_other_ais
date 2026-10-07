import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { chatFastSettingsFile } from '../chatFastSettings';
import { CLAUDE_ARGS, claudeAdapter } from '../claudeAdapter';
import { chatLaunchFor, launchSpecFor } from '../cliChatLaunch';
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
    const file = (on: boolean) => chatFastSettingsFile(on, home);
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
    const path = chatFastSettingsFile(true, home);
    const written = statSync(path).mtimeMs;
    assert.equal(chatFastSettingsFile(true, home), path);
    assert.equal(statSync(path).mtimeMs, written, 'an unchanged file is left alone');
    assert.notEqual(chatFastSettingsFile(false, home), path, 'Off and On are two files');
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
});

test("the chat's own file is never in the chat's empty folder — a private folder of its own, made fresh", () => {
  // The code round: the chat's text-mode folder is "a directory with nothing in it", and a fixed shared temp path is one
  // another local user could plant first.
  const path = chatFastSettingsFile(true);

  assert.match(path, /coai-chat-fast-[^/\\]+[/\\]fast-on\.json$/u);
  assert.equal(readFileSync(path, 'utf8'), '{"fastMode":true}');
  assert.equal(chatFastSettingsFile(true), path, 'one folder for the life of the extension');
});

test('through cmd.exe a settings path with a space reaches claude whole; elsewhere it is passed as it is', () => {
  // The code round: an npm claude.cmd is started with shell: true, which does not quote, so a profile folder with a
  // space would have split the path in two.
  const spaced = 'C:/Users/John Smith/AppData/Local/Temp/coai-chat-fast-x/fast-on.json';
  const vendor = row({ runtime: 'claude', model: 'opus', fast: 'on' });
  const launch = chatLaunchFor(vendor, '', 'claude-opus-5-5', 'text', () => spaced);

  const viaCmd = launchSpecFor(vendor, 'C:/t', launch, 'C:/npm/claude.cmd', 'win32');
  assert.equal(viaCmd.shell, true, 'the fixture: a .cmd shim goes through the shell');
  assert.equal(viaCmd.args[viaCmd.args.indexOf('--settings') + 1], `"${spaced}"`);

  const direct = launchSpecFor(vendor, '/tmp/t', launch, '/usr/bin/claude', 'linux');
  assert.equal(direct.args[direct.args.indexOf('--settings') + 1], spaced);
});

test('through cmd.exe a path with a command operator in it is quoted, and one cmd.exe would still expand is refused', () => {
  // CodeRabbit on #693: a TEMP of `C:\temp&calc&` has no space, so quoting on whitespace alone left `&` to cmd.exe.
  const vendor = row({ runtime: 'claude', model: 'opus', fast: 'on' });
  const via = (path: string) =>
    launchSpecFor(vendor, 'C:/t', chatLaunchFor(vendor, '', 'claude-opus-5-5', 'text', () => path), 'C:/npm/claude.cmd', 'win32');

  const operator = 'C:/temp&calc&/coai-chat-fast-x/fast-on.json';
  const quoted = via(operator);
  assert.equal(quoted.args[quoted.args.indexOf('--settings') + 1], `"${operator}"`);

  for (const expanded of ['C:/temp%PATH%/fast-on.json', 'C:/temp!x!/fast-on.json', 'C:/te"mp/fast-on.json']) {
    const spec = via(expanded);
    assert.deepEqual(spec.args, [], `${expanded}: nothing is handed to the shell`);
    assert.match(spec.refusal, /cmd\.exe/u, `${expanded}: refused, and the refusal names why`);
  }
});
