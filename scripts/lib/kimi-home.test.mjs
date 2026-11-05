// node --test scripts/lib/*.test.mjs — the owned Kimi home of D19 as the probe makes and disposes of it: the junction is
// removed by its own name and never through it, the person's folder keeps its files, the built environment is exactly the
// minimal set plus the two variables, the real-home verdict allows only credentials/ under an owned home, the ~/.agents
// canary takes out exactly what it put in, and Kimi's config and reports parse. Runs nothing of Kimi's; makes folders and
// links only under this user's own temp folder, and removes them.
import assert from 'node:assert/strict';
import { existsSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, readlinkSync, realpathSync, rmSync, statSync, symlinkSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { MINIMAL_UNIX, MINIMAL_WINDOWS } from './vibe.mjs';
import {
  CANDIDATES, agentsCanaryPlan, builtEnv, classifyHomeEntry, credentialNames, disposeOwnedHome, inventory, makeAgentsCanary, makeOwnedHome, minimalConfigToml,
  modelAlias, outputTokens, parseUsageLine, ownedRoot, personsOauth, realHomeVerdict, removeAgentsCanary, shimEnv, versionEnv,
} from './kimi-home.mjs';

const WINDOWS = process.platform === 'win32';
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-kimi-home-test-'));
after(() => rmSync(base, { recursive: true, force: true }));

/** A fake person's home: credentials/ holding two files, one of them read-only. */
function fakeReal(name) {
  const real = join(base, name);
  mkdirSync(join(real, 'credentials'), { recursive: true });
  writeFileSync(join(real, 'credentials', 'kimi-code-env-abc.json'), '{"refresh_token":"r1"}');
  writeFileSync(join(real, 'credentials', 'kimi-code.json'), '{"refresh_token":"r0"}');
  return real;
}

test('the disposal removes the junction by its own name and the person\'s credential files are untouched', { skip: !WINDOWS && 'junctions are Windows' }, () => {
  const real = fakeReal('real-1');
  const home = join(base, 'home-1');
  mkdirSync(home);
  symlinkSync(join(real, 'credentials'), join(home, 'credentials'), 'junction');
  writeFileSync(join(home, 'session_index.jsonl'), '');
  const before = credentialNames(real);
  const r = disposeOwnedHome(home, real, before);
  assert.deepEqual([r.linkRemoved, r.targetIntact, r.homeGone], [true, true, true]);
  assert.equal(existsSync(home), false);
  assert.deepEqual(credentialNames(real), before);
  assert.equal(readFileSync(join(real, 'credentials', 'kimi-code-env-abc.json'), 'utf8'), '{"refresh_token":"r1"}');
});

test('the disposal refuses a home whose credentials is a real folder, and deletes nothing', () => {
  const real = fakeReal('real-2');
  const home = join(base, 'home-2');
  mkdirSync(join(home, 'credentials'), { recursive: true });
  writeFileSync(join(home, 'credentials', 'x.json'), 'x');
  assert.throws(() => disposeOwnedHome(home, real, credentialNames(real)), /not a junction/u);
  assert.equal(existsSync(join(home, 'credentials', 'x.json')), true);
});

test('the inventory names the junction without entering it', { skip: !WINDOWS && 'junctions are Windows' }, () => {
  const real = fakeReal('real-3');
  const home = join(base, 'home-3');
  mkdirSync(home);
  symlinkSync(join(real, 'credentials'), join(home, 'credentials'), 'junction');
  writeFileSync(join(home, 'device_id'), 'd');
  const inv = inventory(home);
  assert.equal(inv.credentials, '(junction)');
  assert.equal(Object.keys(inv).some((f) => f.startsWith('credentials/')), false);
  assert.equal(typeof inv.device_id, 'string');
  unlinkSync(join(home, 'credentials'));
});

test('on Linux the disposal unlinks the credentials SYMLINK by its own name and the person\'s files are untouched', { skip: WINDOWS && 'the Windows arm is the junction test above' }, () => {
  const real = fakeReal('real-linux-1');
  const home = join(base, 'home-linux-1');
  mkdirSync(home);
  symlinkSync(join(real, 'credentials'), join(home, 'credentials'), 'dir');
  writeFileSync(join(home, 'session_index.jsonl'), '');
  const before = credentialNames(real);
  const r = disposeOwnedHome(home, real, before);
  assert.deepEqual([r.linkRemoved, r.targetIntact, r.homeGone], [true, true, true]);
  assert.deepEqual(credentialNames(real), before);
  assert.equal(readFileSync(join(real, 'credentials', 'kimi-code-env-abc.json'), 'utf8'), '{"refresh_token":"r1"}');
});

test('on Linux the owned home\'s credentials is a SYMLINK to the person\'s folder, made at a path that did not exist', { skip: WINDOWS && 'the Windows arm makes a junction through cmd.exe' }, () => {
  const real = fakeReal('real-linux-2');
  writeFileSync(join(real, 'config.toml'), '[providers."managed:kimi-code"]\nbase_url = "https://b"\n\n[providers."managed:kimi-code".oauth]\nkey = "oauth/k"\noauth_host = "https://a"\n');
  const home = join(base, 'home-linux-2');
  const made = makeOwnedHome(home, real, ['config'], { defaultModel: 'kimi-code/k3-256k' });
  assert.equal(made.junctionError, '');
  assert.equal(lstatSync(join(home, 'credentials')).isSymbolicLink(), true);
  assert.equal(readlinkSync(join(home, 'credentials')), join(real, 'credentials'));
  assert.equal(inventory(home).credentials, '(junction)', 'the link is named, never entered');
  disposeOwnedHome(home, real, credentialNames(real));
});

test('the Linux built environment is MinimalUnix + the two variables + the cache folder, PATH joined with ":", and the OS home is HOME alone (DL11)', () => {
  const env = builtEnv({ home: '/r/home', profile: '/r/os', personsBin: '/p/bin', platform: 'linux', parent: { PATH: '/usr/bin', HOME: '/home/u', USER: 'u', APPDATA: 'x' } });
  assert.deepEqual(Object.keys(env).filter((k) => !MINIMAL_UNIX.includes(k)).sort(), ['KIMI_CODE_HOME', 'KIMI_CODE_NO_AUTO_UPDATE', 'XDG_CACHE_HOME']);
  assert.equal(env.HOME, '/r/os');
  assert.equal(env.PATH, '/p/bin:/usr/bin');
  for (const name of ['USERPROFILE', 'HOMEDRIVE', 'HOMEPATH', 'NoDefaultCurrentDirectoryInExePath', 'APPDATA']) assert.equal(name in env, false, name);
  const shim = shimEnv({ root: '/r', personsBin: '/p/bin', platform: 'linux', parent: { PATH: '/usr/bin' } });
  assert.equal(shim.KIMI_CODE_HOME, '/r/home');
  assert.equal(shim.HOME, '/r/os');
  assert.ok(shim.PATH.startsWith('/p/bin:'), shim.PATH);
});

test('the built environment is the minimal set plus exactly the two variables; the pin can be dropped; a profile redirects every home variable', () => {
  const env = builtEnv({ home: 'C:\\h\\kh', platform: 'win32' });
  const extra = Object.keys(env).filter((k) => !MINIMAL_WINDOWS.includes(k));
  assert.deepEqual(extra.sort(), ['KIMI_CODE_HOME', 'KIMI_CODE_NO_AUTO_UPDATE', 'NoDefaultCurrentDirectoryInExePath']);
  assert.equal(env.KIMI_CODE_NO_AUTO_UPDATE, '1');
  assert.equal('KIMI_CODE_NO_AUTO_UPDATE' in builtEnv({ home: 'C:\\h', pin: false, platform: 'win32' }), false);
  const redirected = builtEnv({ home: 'C:\\h', profile: 'C:\\t\\run\\profile', platform: 'win32' });
  assert.equal(redirected.USERPROFILE, 'C:\\t\\run\\profile');
  assert.equal(redirected.HOME, 'C:\\t\\run\\profile');
  assert.equal(redirected.HOMEDRIVE, 'C:');
  assert.equal(redirected.HOMEPATH, '\\t\\run\\profile');
});

test('the real-home verdict: an owned-home launch may touch credentials/ only; a real-home launch Kimi\'s own stores only', () => {
  const owned = realHomeVerdict([{ path: 'credentials/k.json', change: 'changed' }], { owned: true });
  assert.equal(owned.ok, true);
  assert.match(owned.summary, /only credentials/u);
  const bad = realHomeVerdict([{ path: 'credentials/k.json', change: 'changed' }, { path: 'sessions/x/state.json', change: 'added' }], { owned: true });
  assert.equal(bad.ok, false);
  assert.deepEqual(bad.disallowed, ['added sessions/x/state.json']);
  const real = realHomeVerdict([{ path: 'sessions/x/state.json', change: 'added' }, { path: 'updates/latest.json', change: 'changed' }], { owned: false });
  assert.equal(real.ok, true);
  assert.equal(realHomeVerdict([{ path: 'config.toml', change: 'changed' }], { owned: false }).ok, false);
  assert.equal(realHomeVerdict([], { owned: true }).summary, 'unchanged');
});

test('the ~/.agents canary takes out exactly what it put in, and refuses an existing AGENTS.md', () => {
  const osHome = join(base, 'os-home');
  mkdirSync(osHome);
  const plan = agentsCanaryPlan(osHome, { skillName: 'coai-probe-canary-ab12cd', word: 'GANNET-7' });
  assert.deepEqual([plan.madeAgents, plan.madeSkills, plan.madeAgentsMd], [true, true, true]);
  const made = makeAgentsCanary(plan);
  assert.equal(made.length, 2);
  assert.match(readFileSync(plan.skillFile, 'utf8'), /name: coai-probe-canary-ab12cd/u);
  assert.match(readFileSync(plan.agentsMd, 'utf8'), /GANNET-7/u);
  removeAgentsCanary(plan);
  assert.equal(existsSync(plan.agents), false, 'the folder this made is gone with it');

  // A person who already has ~/.agents/skills keeps it: only the canary skill goes.
  const theirs = join(base, 'os-home-2');
  mkdirSync(join(theirs, '.agents', 'skills', 'their-skill'), { recursive: true });
  writeFileSync(join(theirs, '.agents', 'skills', 'their-skill', 'SKILL.md'), 'theirs');
  const plan2 = agentsCanaryPlan(theirs, { skillName: 'coai-probe-canary-ff', word: 'GANNET-7' });
  assert.deepEqual([plan2.madeAgents, plan2.madeSkills, plan2.madeAgentsMd], [false, false, true]);
  makeAgentsCanary(plan2);
  removeAgentsCanary(plan2);
  assert.equal(readFileSync(join(theirs, '.agents', 'skills', 'their-skill', 'SKILL.md'), 'utf8'), 'theirs');
  assert.equal(existsSync(plan2.agentsMd), false);

  // Their own AGENTS.md is never edited.
  writeFileSync(join(theirs, '.agents', 'AGENTS.md'), 'their rules');
  const plan3 = agentsCanaryPlan(theirs, { skillName: 'coai-probe-canary-00', word: 'GANNET-7' });
  assert.throws(() => makeAgentsCanary(plan3), /already exists/u);
  assert.equal(readFileSync(join(theirs, '.agents', 'AGENTS.md'), 'utf8'), 'their rules');
});

test('the person\'s OAuth slot is read off their config.toml, and the minimal config names it and nothing else', () => {
  const config = [
    'default_model = "kimi-code/k3-256k"', '',
    '[providers.moonshot-ai]', 'base_url = "https://api.moonshot.ai/v1"', 'type = "kimi"', 'api_key = "SECRET-A"', '',
    '[providers."managed:kimi-code"]', 'type = "kimi"', 'api_key = "SECRET-B"', 'base_url = "https://api.kimi.ai/coding/v1"', '',
    '[providers."managed:kimi-code".oauth]', 'storage = "file"', 'key = "oauth/kimi-code-env-0123"', 'oauth_host = "https://auth.kimi.ai"', '',
    '[[hooks]]', 'event = "SessionStart"', 'command = "evil"', '',
  ].join('\n');
  const oauth = personsOauth(config);
  assert.deepEqual(oauth, { key: 'oauth/kimi-code-env-0123', oauthHost: 'https://auth.kimi.ai', baseUrl: 'https://api.kimi.ai/coding/v1' });
  const minimal = minimalConfigToml({ ...oauth, defaultModel: 'kimi-code/k3-256k' });
  assert.match(minimal, /key = "oauth\/kimi-code-env-0123"/u);
  assert.doesNotMatch(minimal, /SECRET|api_key|hooks|moonshot-ai|services/u);
  assert.doesNotMatch(minimal, /\[models/u);
  assert.match(minimalConfigToml({ ...oauth, defaultModel: 'kimi-code/k3-256k', withModels: true }), /\[models\."kimi-code\/k3-256k"\]\nprovider = "managed:kimi-code"\nmodel = "k3-256k"/u);
});

test('home entries classify by Kimi\'s data-locations page; an unknown name says so', () => {
  assert.equal(classifyHomeEntry('config.toml'), 'configuration');
  assert.equal(classifyHomeEntry('skills/x/SKILL.md'), 'configuration');
  assert.equal(classifyHomeEntry('plugins/installed.json'), 'configuration');
  assert.equal(classifyHomeEntry('bin/rg.exe'), 'code');
  assert.equal(classifyHomeEntry('sessions/wd_x/s/state.json'), 'data');
  assert.equal(classifyHomeEntry('updates/latest.json'), 'data');
  assert.equal(classifyHomeEntry('device_id'), 'data');
  assert.equal(classifyHomeEntry('credentials'), 'credentials');
  assert.equal(classifyHomeEntry('something-new.json'), 'unknown');
  assert.deepEqual(CANDIDATES, ['config', 'config-models', 'device_id', 'region', 'migrations', 'bin']);
});

test('Kimi\'s reports parse: the /usage line, every outputTokens of a session log, the last modelAlias', () => {
  assert.deepEqual(parseUsageLine('Context: 20004 / 262144 tokens (8%)\nSession total: 19,973 input, 31 output'), { input: 19973, output: 31 });
  assert.equal(parseUsageLine('Session total: no LLM calls yet'), null);
  const log = [
    '2026-10-13T13:51:05.607Z INFO  llm config  turnStep=0.1 provider=openai model=k3-256k modelAlias=kimi-code/k3-256k thinkingEffort=high',
    '2026-10-13T13:51:17.091Z INFO  llm response  turnStep=0.1 ttftMs=8159 streamDurationMs=3308 outputTokens=126 requestBuildMs=35',
    '2026-10-13T13:52:05.607Z INFO  llm config  turnStep=1.1 provider=openai model=k3 modelAlias=kimi-code/k3 thinkingEffort=high',
    '2026-10-13T13:52:17.091Z INFO  llm response  turnStep=1.1 ttftMs=8159 outputTokens=7 requestBuildMs=35',
  ].join('\n');
  assert.deepEqual(outputTokens(log), [126, 7]);
  assert.equal(modelAlias(log), 'kimi-code/k3');
  assert.equal(modelAlias(''), '');
  assert.equal(statSync(base).isDirectory(), true);
});

// ------------------------------------------------------------------------------------------------------ the cache folder beside os/ (S-N3)
// DL11: Kimi extracts about 2 MB of native files on every launch into $XDG_CACHE_HOME, else $HOME/.cache — inside os/, which must stay EMPTY.
// The product names its own cache/ folder (KimiEnvironment.cs:34, :94; KimiPlaces.Cache = <root>/cache); the probe's Linux built environment must,
// or a direct `kimi -p` control or `kimi --version` fills os/ — an artefact that would read as a product failure.

test('S-N3: the Linux built environment names XDG_CACHE_HOME as the cache folder beside os/ — for the shim and for a direct launch', () => {
  const direct = builtEnv({ home: '/r/home', profile: '/r/os', platform: 'linux', parent: { PATH: '/usr/bin' } });
  const shim = shimEnv({ root: '/r', personsBin: '/p/bin', platform: 'linux', parent: { PATH: '/usr/bin' } });

  assert.equal(direct.XDG_CACHE_HOME, '/r/cache', 'the Linux built environment has no XDG_CACHE_HOME — a direct kimi launch fills os/ (DL11)');
  assert.equal(shim.XDG_CACHE_HOME, '/r/cache');
});

// J2 of the Opus judge on E1: this test used to pin "no profile -> no cache variable", which let every launch with the REAL OS home (M-K12 (a) and its
// control, N-K5 / M-K11c's `kimi --version`) extract Kimi's native files into the person's real ~/.cache. The cache is named whenever the home is an owned
// `<root>/home`, with or without the OS-home redirect; a home that is not an owned one still gets none.
test('J2: an owned <root>/home names XDG_CACHE_HOME even with no OS-home redirect (HOME stays the parent one); a home that is not <root>/home gets none; Windows never has one', () => {
  const parent = { PATH: '/usr/bin', HOME: '/home/op' };
  const real = builtEnv({ home: '/r/home', platform: 'linux', parent });
  assert.equal(real.XDG_CACHE_HOME, '/r/cache', 'a launch on an owned home with the real OS home writes the native files of Kimi into the real ~/.cache of the person');
  assert.equal(real.HOME, '/home/op', 'the real OS home is kept — M-K12 (a) measures it');
  assert.equal('XDG_CACHE_HOME' in builtEnv({ home: '/somewhere/else', platform: 'linux', parent }), false, 'not an owned root: nothing to name');
  assert.equal('XDG_CACHE_HOME' in builtEnv({ home: 'C:/r/home', profile: 'C:/r/os', platform: 'win32', parent: {} }), false);
});

test('G0: an explicit cacheDir names the cache folder, over the derivations; the default keeps the derivation beside os/', () => {
  const parent = { PATH: '/usr/bin' };
  assert.equal(builtEnv({ home: '/r/home', profile: '/r/os', cacheDir: '/elsewhere/cache', platform: 'linux', parent }).XDG_CACHE_HOME, '/elsewhere/cache');
  assert.equal(builtEnv({ home: '/x/home', cacheDir: '/elsewhere/cache', platform: 'linux', parent }).XDG_CACHE_HOME, '/elsewhere/cache', 'even for a home that is no owned root');
  assert.equal(builtEnv({ home: '/r/home', profile: '/r/os', platform: 'linux', parent }).XDG_CACHE_HOME, '/r/cache');
  assert.equal('XDG_CACHE_HOME' in builtEnv({ home: 'C:/r/home', cacheDir: 'C:/c', platform: 'win32', parent: {} }), false);
});

test('S-N3: an owned root of a Linux host also holds the cache folder beside os/ (owner-only), as KimiHome.MakeCache makes it; a Windows root does not', () => {
  const made = ownedRoot(join(base, 'kh-cache'), { platform: 'linux' });

  assert.equal(made.cache, join(made.root, 'cache'));
  assert.equal(existsSync(made.cache), true, 'the Linux owned root has no cache folder beside os/');
  assert.deepEqual(readdirSync(made.os), [], 'os/ must stay empty');
  assert.equal(existsSync(ownedRoot(join(base, 'kh-nocache'), { platform: 'win32' }).cache ?? ''), false, 'Windows has no such variable and no such folder');
});

test('the ~/.agents canary is removed by its own exact paths only: a stranger\'s file inside the canary folder survives, and the removal says so instead of deleting it', () => {
  const osHome = join(base, 'os-home-stranger');
  mkdirSync(osHome);
  const plan = agentsCanaryPlan(osHome, { skillName: 'coai-probe-canary-ee11', word: 'GANNET-7' });
  makeAgentsCanary(plan);
  const stranger = join(plan.skillDir, 'not-ours.txt');
  writeFileSync(stranger, 'somebody else\'s');

  assert.throws(() => removeAgentsCanary(plan), /still there|not empty|ENOTEMPTY/u, 'a removal that cannot be exact must fail loudly');
  assert.equal(readFileSync(stranger, 'utf8'), 'somebody else\'s', 'the removal deleted a file it did not make (a recursive delete)');
  assert.equal(existsSync(plan.skillFile), false, 'the canary\'s own file is removed');
  rmSync(stranger);
  removeAgentsCanary(plan);
  assert.equal(existsSync(plan.agents), false);
});

// J4 of the Opus judge on E1: the "does it exist" check ran at PLAN time, the write came later - a file the owner created in between was overwritten.
test('J4: makeAgentsCanary never overwrites an AGENTS.md the owner created after the plan was made, and leaves nothing of its own behind', () => {
  const osHome = join(base, 'os-home-race');
  mkdirSync(osHome);
  const plan = agentsCanaryPlan(osHome, { skillName: 'coai-probe-canary-ff22', word: 'GANNET-7' });
  mkdirSync(plan.agents, { recursive: true });
  writeFileSync(plan.agentsMd, 'the owner wrote this after the start check');

  assert.throws(() => makeAgentsCanary(plan), /EEXIST|exists/u);

  assert.equal(readFileSync(plan.agentsMd, 'utf8'), 'the owner wrote this after the start check', "the canary overwrote the owner's AGENTS.md");
  assert.equal(existsSync(plan.skillDir), false, 'the canary skill it had already written was not taken back');
});

test('J4: makeAgentsCanary writes both files with an exclusive create - a skill file that appeared after the plan stops it and is left alone', () => {
  const osHome = join(base, 'os-home-race2');
  mkdirSync(osHome);
  const plan = agentsCanaryPlan(osHome, { skillName: 'coai-probe-canary-ff23', word: 'GANNET-7' });
  mkdirSync(plan.skillDir, { recursive: true });
  writeFileSync(plan.skillFile, 'not ours');

  assert.throws(() => makeAgentsCanary(plan), /exists/u);
  assert.equal(readFileSync(plan.skillFile, 'utf8'), 'not ours');
  assert.equal(existsSync(plan.agentsMd), false);
});

// Re-check of J2: the probe's own `kimi --version` (before any cell) ran with NO env - the real HOME, the real ~/.kimi-code, no pin - so Kimi extracted into the
// person's real cache. It now runs in the built environment of an owned root.
test('re-check J2: the version launch runs in an owned root\'s built environment - KIMI_CODE_HOME, the cache folder and the pin named, HOME untouched', () => {
  const env = versionEnv('/r/version', { platform: 'linux', parent: { PATH: '/usr/bin', HOME: '/home/op', SECRET: 'x' } });

  assert.equal(env.KIMI_CODE_HOME, '/r/version/home', 'the version launch has no owned KIMI_CODE_HOME - it reads the real ~/.kimi-code');
  assert.equal(env.XDG_CACHE_HOME, '/r/version/cache', 'the version launch has no owned XDG_CACHE_HOME - Kimi extracts into the real cache');
  assert.equal(env.KIMI_CODE_NO_AUTO_UPDATE, '1');
  assert.equal(env.HOME, '/home/op');
  assert.equal('SECRET' in env, false, 'nothing is inherited but the minimal names');
});
