// lib/kimi-home.mjs — the OWNED Kimi home of D19 (todo/PLAN_vibe_and_kimi_runtimes.md), as the measurement script makes,
// watches and disposes of it: a folder holding the person's sign-in by JUNCTION and nothing copied, a built environment,
// a junction-first disposal, and the before/after comparison of the person's own ~/.kimi-code that M-K11 (b) is.
//
// Everything here is the probe's; the product writes the same shapes in C# (KimiHome, KimiLaunch) AFTER these cells are
// recorded — the cells gate the code (plan §4 S4). Every FACT about Kimi below names the measurement or document it came from.
//
// The person's home is sacred: nothing here writes into ~/.kimi-code — the only writes into it are Kimi's own, through the
// junction (the token refresh, RESULTS §11). A junction is removed by its OWN name (rmdirSync on the link), never through it;
// after every disposal the target folder must still hold the files it held.
import { copyFileSync, existsSync, lstatSync, mkdirSync, readdirSync, readFileSync, rmSync, rmdirSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, parse } from 'node:path';
import { files, fwd, minimalWindowsEnv } from './vibe.mjs';
import { fileHash, junction } from './kimi.mjs';

/** What M-K11 (a) may admit into an owned home, one at a time, in the plan's order (§5) — the junction is always there. */
export const CANDIDATES = ['config', 'config-models', 'device_id', 'region', 'migrations', 'bin'];

const isLink = (p) => { try { return lstatSync(p).isSymbolicLink(); } catch { return false; } };

// ---------------------------------------------------------------------------------------------- the person's provider

/**
 * The managed provider's OAuth slot as the person's config.toml names it — READ, never copied: `key`, `oauth_host` and
 * `base_url` of `[providers."managed:kimi-code"]`. Kimi's own default slot is `oauth/kimi-code` for the region its marker
 * file names (the binary's resolveKimiRegion, read 2026-10-13), so a home with no config may open a DIFFERENT credential
 * file than the person's own launch does; a coai-written config naming the person's slot is M-K11 (a)'s first candidate.
 */
export function personsOauth(configText) {
  const section = (header) => {
    const at = configText.indexOf(header);
    if (at < 0) return '';
    const rest = configText.slice(at + header.length);
    const next = rest.search(/\n\s*\[/u);
    return next < 0 ? rest : rest.slice(0, next);
  };
  const value = (text, name) => text.match(new RegExp(`^\\s*${name}\\s*=\\s*"([^"]*)"`, 'mu'))?.[1] ?? '';
  const provider = section('[providers."managed:kimi-code"]');
  const oauth = section('[providers."managed:kimi-code".oauth]');
  return { key: value(oauth, 'key'), oauthHost: value(oauth, 'oauth_host'), baseUrl: value(provider, 'base_url') };
}

/**
 * The minimal config.toml coai would write into an owned home: the managed provider by name, its OAuth slot (the
 * person's, so the junctioned credential file is the one their own launch uses), the default model — and nothing else.
 * No api_key, no hooks, no services. `withModels` adds the one model block the launch names, for the arm that finds
 * Kimi's built-in catalog does not supply it.
 */
export function minimalConfigToml({ key, oauthHost, baseUrl, defaultModel, withModels = false }) {
  const provider = [
    `default_model = "${defaultModel}"`, '',
    '[providers."managed:kimi-code"]', 'type = "kimi"', `base_url = "${baseUrl}"`, '',
    '[providers."managed:kimi-code".oauth]', 'storage = "file"', `key = "${key}"`, `oauth_host = "${oauthHost}"`, '',
  ];
  const model = withModels
    ? [`[models."${defaultModel}"]`, 'provider = "managed:kimi-code"', `model = "${defaultModel.split('/').at(-1)}"`, 'max_context_size = 262144',
      'capabilities = [ "thinking", "always_thinking", "image_in", "tool_use", "dynamically_loaded_tools" ]', '']
    : [];
  return `${[...provider, ...model].join('\n')}`;
}

// ------------------------------------------------------------------------------------------------------ the owned home

/** The names the person's credentials folder holds — recorded before a junction is made, checked after every disposal. */
export const credentialNames = (real) => (existsSync(join(real, 'credentials')) ? readdirSync(join(real, 'credentials')).sort((a, b) => a.localeCompare(b)) : []);

/**
 * An owned home at `home`: `credentials` is a junction to the person's, and `admitted` (a subset of CANDIDATES) says what
 * else it holds — written by coai (the config) or copied from the person's home by name, verified by hash. Nothing else.
 * Returns what it holds and the junction's error text when one could not be made.
 */
export function makeOwnedHome(home, real, admitted, { defaultModel }) {
  if (admitted.some((c) => !CANDIDATES.includes(c))) throw new Error(`unknown owned-home candidate in ${admitted.join(',')}`);
  if (!existsSync(join(real, 'credentials'))) throw new Error(`${real} has no credentials/ to junction — not signed in`);
  if (isLink(join(real, 'credentials'))) throw new Error(`${real}/credentials is itself a link — never chained`);
  mkdirSync(home, { recursive: true, mode: 0o700 });
  const holds = [];
  const link = junction(join(home, 'credentials'), join(real, 'credentials'));
  if (link.length > 0) return { holds, junctionError: link };
  if (!isLink(join(home, 'credentials'))) return { holds, junctionError: 'credentials is not a link after mklink' };
  holds.push('credentials (junction)');
  const copy = (rel) => {
    const from = join(real, rel);
    if (!existsSync(from)) return;
    mkdirSync(dirname(join(home, rel)), { recursive: true });
    copyFileSync(from, join(home, rel));
    if (fileHash(from) !== fileHash(join(home, rel))) throw new Error(`copy of ${rel} does not hash like its source`);
    holds.push(`${rel} (copied)`);
  };
  if (admitted.includes('config') || admitted.includes('config-models')) {
    const oauth = personsOauth(readFileSync(join(real, 'config.toml'), 'utf8'));
    writeFileSync(join(home, 'config.toml'), minimalConfigToml({ ...oauth, defaultModel, withModels: admitted.includes('config-models') }));
    holds.push(`config.toml (coai-written${admitted.includes('config-models') ? ', with the model block' : ''})`);
  }
  if (admitted.includes('device_id')) copy('device_id');
  if (admitted.includes('region')) copy('region');
  if (admitted.includes('migrations')) copy('migrations-effort.json');
  if (admitted.includes('bin')) for (const f of ['rg.exe', 'fd.exe']) copy(join('bin', f));
  return { holds, junctionError: '' };
}

/**
 * D19's disposal order, exactly: (1) the `credentials` link removed by its own name with a NON-recursive delete, (2) the
 * link verified gone AND the person's folder verified to still hold every name it held, (3) only then the rest of the home.
 * Throws — and deletes nothing more — the moment a check fails.
 */
export function disposeOwnedHome(home, real, namesBefore) {
  const link = join(home, 'credentials');
  const result = { home, linkRemoved: false, targetIntact: false, homeGone: false };
  if (isLink(link)) {
    rmdirSync(link);
    if (isLink(link) || existsSync(link)) throw new Error(`${link} still exists after rmdir — nothing else was deleted`);
    result.linkRemoved = true;
  } else if (existsSync(link)) {
    throw new Error(`${link} is a real folder, not a junction — nothing was deleted`);
  }
  const now = credentialNames(real);
  const missing = namesBefore.filter((n) => !now.includes(n));
  if (missing.length > 0) throw new Error(`the person's credentials lost ${missing.join(', ')} — nothing else was deleted`);
  result.targetIntact = true;
  // The walk below never meets a reparse point: the only link an owned home holds is gone.
  for (const f of files(home)) if (isLink(join(home, f))) throw new Error(`${join(home, f)} is a link — refusing a recursive delete`);
  rmSync(home, { recursive: true, force: true });
  result.homeGone = !existsSync(home);
  return result;
}

/** Every file of an owned home with its hash — the `credentials` junction named but never entered (M-K11 (c)'s inventory). */
export function inventory(home) {
  const out = {};
  for (const f of files(home)) {
    const p = join(home, f);
    out[f] = isLink(p) ? '(junction)' : fileHash(p);
  }
  return out;
}

/**
 * What a name in an owned home IS, by Kimi's data-locations page (docs/en/configuration/data-locations.md, 2026-10-13)
 * and the plan's validation list: configuration or code Kimi later READS (the manifest question), data it writes, or the
 * credentials. `unknown` is a name neither document has — it goes into the row for the plan's owner.
 */
export function classifyHomeEntry(path) {
  const p = fwd(path);
  if (p === 'credentials' || p.startsWith('credentials/')) return 'credentials';
  if (/^(config\.toml|tui\.toml|mcp\.json|local\.toml|AGENTS\.md|agents\.md|statusline[^/]*)$/u.test(p) || /^(skills|agents|plugins|themes|hooks)\//u.test(p)) return 'configuration';
  if (/^bin\//u.test(p)) return 'code';
  if (/^(sessions\/|session_index\.jsonl$|workspaces\.json$|workspace-trust\/|cache\/|logs\/|telemetry\/|user-history\/|file-history\/|updates\/|device_id$|region$|migrations-effort\.json$)/u.test(p)) return 'data';
  return 'unknown';
}

// ------------------------------------------------------------------------------------- the person's home, watched

/** Kimi's own stores in the PERSON's home — what a launch ON the real home may write (RESULTS §3). */
export const VENDOR_WRITTEN = /^(sessions\/|session_index\.jsonl$|workspaces\.json$|logs\/|user-history\/|telemetry\/|cache\/|credentials\/|updates\/|workspace-trust\/|file-history\/)/u;

/**
 * M-K11 (b)'s judgement of one launch: the person's home may differ ONLY in `credentials/` (the refresh through the
 * junction) when the launch ran on an owned home, and only in Kimi's own stores when it ran on the real home. Anything
 * else is a stop, not a row.
 */
export function realHomeVerdict(diff, { owned }) {
  const allowedBy = owned ? (p) => p.startsWith('credentials/') : (p) => VENDOR_WRITTEN.test(p);
  const allowed = diff.filter((d) => allowedBy(d.path)).map((d) => `${d.change} ${d.path}`);
  const disallowed = diff.filter((d) => !allowedBy(d.path)).map((d) => `${d.change} ${d.path}`);
  return { ok: disallowed.length === 0, allowed, disallowed, summary: disallowed.length ? `CHANGED outside the allowed set: ${disallowed.join(', ')}` : (allowed.length ? `only ${owned ? 'credentials/' : "Kimi's own stores"} (${allowed.length})` : 'unchanged') };
}

// ---------------------------------------------------------------------------------------------- the built environment

/**
 * D19's environment, built and never inherited: ProcessEnvironment.Minimal (lib/vibe.mjs's copy of the C# list) plus
 * exactly `KIMI_CODE_HOME` and `KIMI_CODE_NO_AUTO_UPDATE=1` (documented by Kimi: "fully disable the update preflight",
 * docs/en/configuration/env-vars.md). `pin: false` is M-K13's control; `profile` redirects `USERPROFILE`/`HOME`/
 * `HOMEDRIVE`/`HOMEPATH` to a folder — M-K12 (b)'s question whether that closes `~/.agents/*`.
 */
export function builtEnv({ home, pin = true, profile = '' }) {
  const env = { ...minimalWindowsEnv(), NoDefaultCurrentDirectoryInExePath: '1', KIMI_CODE_HOME: home };
  if (pin) env.KIMI_CODE_NO_AUTO_UPDATE = '1';
  if (profile) {
    const { root, dir, base } = parse(profile);
    Object.assign(env, { USERPROFILE: profile, HOME: profile, HOMEDRIVE: root.replace(/[\\/]+$/u, ''), HOMEPATH: `${dir.slice(root.length - 1)}\\${base}`.replace(/^\\+/u, '\\') });
  }
  return env;
}

// ----------------------------------------------------------------------------------------------- the ~/.agents canary

/**
 * M-K12's canary in the person's REAL `~/.agents/` — allowed by the owner on 2026-10-13, behind `--allow-agents-canary`.
 * The plan records what exists BEFORE so the removal takes exactly what was made: a skill folder and an AGENTS.md line,
 * and the `skills/` and `.agents/` folders only when this made them.
 */
export function agentsCanaryPlan(osHome, { skillName, word }) {
  const agents = join(osHome, '.agents');
  const skills = join(agents, 'skills');
  const agentsMd = join(agents, 'AGENTS.md');
  return {
    agents, skills, skillDir: join(skills, skillName), skillFile: join(skills, skillName, 'SKILL.md'), agentsMd, skillName, word,
    madeAgents: !existsSync(agents), madeSkills: !existsSync(skills), madeAgentsMd: !existsSync(agentsMd),
  };
}

export function makeAgentsCanary(plan) {
  if (!plan.madeAgentsMd) throw new Error(`${plan.agentsMd} already exists — the canary would edit the person's own file; refusing`);
  if (existsSync(plan.skillDir)) throw new Error(`${plan.skillDir} already exists; refusing`);
  mkdirSync(plan.skillDir, { recursive: true });
  writeFileSync(plan.skillFile, `---\nname: ${plan.skillName}\ndescription: A probe skill. Mention its name when asked which skills you have.\n---\nSay ${plan.word}-S.\n`);
  writeFileSync(plan.agentsMd, `# Probe\n\nBegin every answer with the word ${plan.word}.\n`);
  return [plan.skillFile, plan.agentsMd];
}

/** Removes exactly what makeAgentsCanary made and verifies it is gone; returns what was removed. */
export function removeAgentsCanary(plan) {
  const removed = [];
  const drop = (p, dir = false) => { if (!existsSync(p)) return; if (dir) rmdirSync(p); else rmSync(p); removed.push(basename(p)); };
  drop(plan.skillFile);
  drop(plan.skillDir, true);
  drop(plan.agentsMd);
  if (plan.madeSkills && existsSync(plan.skills) && readdirSync(plan.skills).length === 0) drop(plan.skills, true);
  if (plan.madeAgents && existsSync(plan.agents) && readdirSync(plan.agents).length === 0) drop(plan.agents, true);
  const left = [plan.skillFile, plan.skillDir, plan.agentsMd, ...(plan.madeAgents ? [plan.agents] : [])].filter((p) => existsSync(p));
  if (left.length > 0) throw new Error(`the ~/.agents canary is still there: ${left.join(', ')}`);
  return removed;
}

// ------------------------------------------------------------------------------------------------- what Kimi reports

/** `Session total: N input, M output` out of a `/usage` answer (RESULTS §5), or null. */
export function parseUsageLine(text) {
  const m = String(text ?? '').match(/Session total:\s*([\d,]+)\s*input,\s*([\d,]+)\s*output/u);
  return m ? { input: Number(m[1].replaceAll(',', '')), output: Number(m[2].replaceAll(',', '')) } : null;
}

/** Every `outputTokens=N` of a session log's `llm response` lines, in order — one per model call. */
export const outputTokens = (logText) => [...String(logText ?? '').matchAll(/llm response.*?outputTokens=(\d+)/gu)].map((m) => Number(m[1]));

/** The LAST `modelAlias=` of a session log's `llm config` lines — the model the session actually ran on. */
export const modelAlias = (logText) => [...String(logText ?? '').matchAll(/llm config.*?modelAlias=(\S+)/gu)].at(-1)?.[1] ?? '';
