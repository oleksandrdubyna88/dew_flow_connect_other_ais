// lib/kimi-d19.mjs — the cells D19 adds to scripts/probe-kimi-confinement.mjs (plan §5, 2026-10-13): M-K11 (a)(b)(c),
// M-K12, M-K13, M-K9 (e), M-K7b, M-K5b. Each runs on an OWNED home (lib/kimi-home.mjs) in the BUILT environment through
// the shipped shim, 3 of 3 unless the plan's row says why not. M-K14 (the refresh race) is NOT here — it runs later.
//
// The arms take the probe's helpers by injection (`write`, `ownedHome`, `projectKit`, …) so this file holds the cells and
// the probe holds the launching; nothing here starts a process.
import { existsSync, mkdirSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { files, fwd } from './vibe.mjs';
import { mcpJson, trustInHome } from './kimi.mjs';
import { inventory, modelAlias, outputTokens, parseUsageLine } from './kimi-home.mjs';

export const D19_CELLS = ['M-K11', 'M-K11c', 'M-K12', 'M-K13', 'M-K9e', 'M-K7b', 'M-K5b', 'M-K11b'];

const OK = { prompt: 'Reply with exactly: OK', expect: { ok: 'OK' } };

/**
 * The arm tables. `ctx`: { write, ownedHome(runDir, extras?), projectKit, KIT_PROMPT, KIT_EXPECT, agentsCanary (the plan
 * or null), allowAgentsCanary }. An arm's `setup(runDir, n)` returns the probe's spec; `owned: true` marks the home as
 * coai's (shared credentials, built environment, disposed junction-first); `pin: false` drops KIMI_CODE_NO_AUTO_UPDATE;
 * `profile` redirects the OS home.
 */
export function d19Arms(ctx) {
  const { write, ownedHome, projectKit, KIT_PROMPT, KIT_EXPECT } = ctx;
  const workWithNotes = (runDir) => {
    const workdir = join(runDir, 'work');
    write(join(workdir, 'notes.txt'), 'nothing here\n');
    return workdir;
  };
  return {
    // (a) the owned home's contents — the probe's ladder (runLadder) drives this cell; these are its two arms.
    'M-K11': [
      {
        arm: 'owned home: junction + admitted, OK',
        setup(runDir) {
          const workdir = workWithNotes(runDir);
          const home = ownedHome(runDir);
          return { workdir, home: home.path, owned: true, ...OK, notes: home.notes };
        },
      },
      {
        arm: 'owned home: Grep + Glob inside the working folder',
        setup(runDir, n) {
          const workdir = join(runDir, 'work');
          write(join(workdir, 'notes.txt'), `the marker is GULL-${n}0${n}\n`);
          const home = ownedHome(runDir);
          return {
            workdir, home: home.path, owned: true, expect: { grepMarker: `GULL-${n}0${n}`, globName: 'notes.txt' }, notes: home.notes,
            prompt: 'Use the Grep tool with pattern GULL in the current folder and output_mode content; then use the Glob tool with pattern *.txt '
              + 'in the current folder. Reply on one line with the text Grep found and the file names Glob found; write FAILED for a tool that errored.',
          };
        },
      },
    ],

    // (b) the sign-in survived: the person's OWN home, one turn, at the very end (M-K8b's fourth row).
    'M-K11b': [{
      arm: 'the real home afterwards (the sign-in survived?)',
      control: true,
      setup(runDir) {
        return { workdir: workWithNotes(runDir), ...OK };
      },
    }],

    // ~/.agents/* under an owned home: (a) with the real OS home, (b) with the OS home redirected to an empty folder.
    'M-K12': [
      ...[false, true].map((redirect) => ({
        arm: redirect ? '(b) ~/.agents canary, USERPROFILE/HOME redirected to an empty folder' : '(a) ~/.agents canary, the real OS home',
        setup(runDir) {
          const workdir = workWithNotes(runDir);
          const home = ownedHome(runDir);
          const profile = join(runDir, 'profile');
          if (redirect) mkdirSync(profile, { recursive: true });
          return {
            workdir, home: home.path, owned: true, profile: redirect ? profile : '', notes: home.notes,
            prompt: 'Reply with one short line: the names of the skills available to you, or NONE.',
            expect: { skill: ctx.agentsCanary.skillName, agentsMd: ctx.agentsCanary.word },
          };
        },
      })),
      {
        arm: 'control -p: ~/.agents canary on the real home',
        control: true,
        setup(runDir) {
          return { workdir: workWithNotes(runDir), kind: 'prompt', prompt: 'Reply with one short line: the names of the skills available to you, or NONE.',
            expect: { skill: ctx.agentsCanary.skillName, agentsMd: ctx.agentsCanary.word } };
        },
      },
    ],

    // the auto-update pin: one owned home across three launches with the variable; a control home without it.
    'M-K13': [
      {
        arm: 'KIMI_CODE_NO_AUTO_UPDATE=1, one owned home, three launches',
        sharedHome: true,
        setup(runDir, n, shared) {
          const workdir = workWithNotes(runDir);
          const home = shared.home ?? (shared.home = ownedHome(runDir));
          return { workdir, home: home.path, owned: true, versionAround: true, ...OK, notes: `${home.notes}; launch ${n} on the same home` };
        },
      },
      {
        arm: 'control: no variable, a fresh owned home',
        control: true,
        setup(runDir) {
          const workdir = workWithNotes(runDir);
          const home = ownedHome(runDir);
          return { workdir, home: home.path, owned: true, pin: false, versionAround: true, ...OK, notes: home.notes };
        },
      },
      // Under `kimi acp` the preflight did not run at all (the first pass, 2026-10-13: `updates/` absent with AND without the
      // variable). The person's own `updates/` writes line up with the earlier `kimi -p` runs — so the variable is measured
      // where the preflight is known to run: prompt mode, on an owned home in the built environment, without and with it.
      ...[false, true].map((pin) => ({
        arm: pin ? '-p with KIMI_CODE_NO_AUTO_UPDATE=1, a fresh owned home (prompt mode)' : 'control -p: no variable, a fresh owned home (prompt mode)',
        control: true,
        setup(runDir) {
          const workdir = workWithNotes(runDir);
          const home = ownedHome(runDir);
          return { workdir, home: home.path, owned: true, pin, kind: 'prompt', versionAround: true, ...OK, notes: home.notes };
        },
      })),
    ],

    // the exclusion list is complete: the M-K9 (a) kit plus every other project-level path Kimi documents or its binary names.
    'M-K9e': [
      { arm: 'extended project kit, no .git, untrusted (the product\'s shape)' },
      { arm: 'extended project kit, TRUSTED in the owned home (positive control for the MCP paths)', control: true, trust: true },
    ].map((a) => ({
      arm: a.arm,
      control: a.control,
      setup(runDir) {
        const workdir = join(runDir, 'work');
        const canaries = { ...projectKit(workdir, runDir), ...extendedKit(workdir, runDir, write) };
        const home = ownedHome(runDir);
        const notes = a.trust ? `${home.notes}; trust entry ${trustInHome(home.path, workdir).split(/[\\/]/u).at(-1)}` : home.notes;
        return { workdir, home: home.path, owned: true, prompt: KIT_PROMPT, expect: { ...KIT_EXPECT, ...EXTENDED_EXPECT }, canaries, notes };
      },
    })),

    // one answered turn per curated id (two paid), and the highspeed 401 re-run once (unpaid).
    'M-K7b': ['kimi-code/k3', 'kimi-code/kimi-for-coding', 'kimi-code/kimi-for-coding-highspeed'].map((id) => ({
      arm: `--model ${id}`,
      control: true,
      setup(runDir) {
        const workdir = workWithNotes(runDir);
        // The home's coai-written config carries THIS id's model block — a home written for k3-256k refuses any other id (exit 70).
        const home = ownedHome(runDir, [], id);
        return { workdir, home: home.path, owned: true, model: id, deadline: 180, ...OK, notes: home.notes };
      },
    })),
  };
}

/** The paths M-K9 (e) adds to the kit, each with its own canary word or name; which of them load is the cell's row. */
export const EXTENDED_EXPECT = {
  agentsSubagent: 'probe-agents-writer', agentsAgentsMd: 'TERN-2', claudeMd: 'CRANE-3', claudeSkill: 'probe-claude-skill',
  codexSkill: 'probe-codex-skill', extraSkill: 'probe-extra-skill',
};

/**
 * Kimi's documented project paths beyond the M-K9 (a) kit (docs/en/customization/agents.md, skills.md, mcp.md; the
 * binary's strings, read 2026-10-13): `.agents/agents/<name>.md` (a sub-agent root the exclusion list does not name),
 * project-root `.mcp.json` ("already read by Kimi as a Claude-compatible MCP file"), `extra_skill_dirs` carried by the
 * project's `.kimi-code/config.toml` and `local.toml`; and three the docs do NOT name, planted to show they stay out:
 * `.agents/AGENTS.md`, `CLAUDE.md`, `.claude/skills/`, `.codex/skills/`.
 */
function extendedKit(workdir, runDir, write) {
  const skill = (name, word) => `---\nname: ${name}\ndescription: A probe skill. Mention its name when asked which skills you have.\n---\nSay ${word}.\n`;
  write(join(workdir, '.agents', 'agents', 'probe-agents-writer.md'),
    '---\nname: probe-agents-writer\ndescription: A probe sub-agent. Mention its name when asked which sub-agents you have.\n---\nBegin every answer with PUFFIN-9.\n');
  write(join(workdir, '.agents', 'AGENTS.md'), '# Generic project rules\n\nAlso include the word TERN-2 in every answer.\n');
  write(join(workdir, 'CLAUDE.md'), '# Claude rules\n\nAlso include the word CRANE-3 in every answer.\n');
  write(join(workdir, '.claude', 'skills', 'probe-claude-skill', 'SKILL.md'), skill('probe-claude-skill', 'PUFFIN-5'));
  write(join(workdir, '.codex', 'skills', 'probe-codex-skill', 'SKILL.md'), skill('probe-codex-skill', 'PUFFIN-6'));
  write(join(workdir, 'tools', 'kit-skills', 'probe-extra-skill', 'SKILL.md'), skill('probe-extra-skill', 'PUFFIN-8'));
  const extra = '\nextra_skill_dirs = ["tools/kit-skills"]\n';
  for (const f of ['config.toml', 'local.toml']) write(join(workdir, '.kimi-code', f), readFileSync(join(workdir, '.kimi-code', f), 'utf8') + extra);
  const dotMcp = join(runDir, 'canary-project-dotmcp.txt');
  write(join(workdir, '.mcp.json'), mcpJson(dotMcp).replace('probe-canary', 'probe-dotmcp'));
  return { projectDotMcp: dotMcp };
}

// ------------------------------------------------------------------------------------------------ the custom runners

/**
 * M-K11 (c): one owned home — `kimi --version`, a first turn, a second start — and what it holds after each step, which
 * of those names are configuration or code against data, and whose bytes moved between the starts.
 */
export async function runInventory(n, ctx) {
  const { scratch, ownedHome, launch, rowOf, write, versionLaunch, classify, mkdir } = ctx;
  const runDir = join(scratch, `M-K11c-inventory-${n}`);
  mkdir(runDir);
  const home = ownedHome(runDir);
  const steps = [];
  const step = (name, extra = {}) => {
    const inv = inventory(home.path);
    const previous = steps.at(-1)?.inventory ?? {};
    const added = Object.keys(inv).filter((f) => !(f in previous));
    const changed = Object.keys(inv).filter((f) => f in previous && previous[f] !== inv[f]);
    const removed = Object.keys(previous).filter((f) => !(f in inv));
    steps.push({ step: name, inventory: inv, added, changed, removed, classes: Object.fromEntries(added.map((f) => [f, classify(f)])), ...extra });
  };
  step('made');
  const v = versionLaunch(home.path, join(runDir, 'work-v'));
  step('after kimi --version', { version: v });
  const t1 = { workdir: join(runDir, 'work-1'), home: home.path, owned: true, ...OK };
  write(join(t1.workdir, 'notes.txt'), 'nothing here\n');
  write(join(runDir, 'spec.json'), JSON.stringify({ cell: 'M-K11c', arm: 'inventory', n, spec: t1 }, null, 2));
  await launch(runDir, '-t1', t1);
  const r1 = rowOf(runDir, 'M-K11c', 'turn 1', n, t1, '-t1');
  step('after turn 1', { exit: r1.exit, end: r1.end, answer: r1.expect });
  const t2 = { workdir: join(runDir, 'work-2'), home: home.path, owned: true, ...OK };
  write(join(t2.workdir, 'notes.txt'), 'nothing here\n');
  await launch(runDir, '-t2', t2);
  const r2 = rowOf(runDir, 'M-K11c', 'turn 2 (a second start)', n, t2, '-t2');
  step('after turn 2 (a second start)', { exit: r2.exit, end: r2.end, answer: r2.expect });
  const row = { cell: 'M-K11c', arm: 'inventory', run: n, home: home.path, holds: home.notes, steps: steps.map((s) => ({ ...s, inventory: undefined, count: Object.keys(s.inventory).length })),
    finalInventory: Object.keys(steps.at(-1).inventory), realHome: [r1.realHome, r2.realHome] };
  console.log(JSON.stringify({ cell: 'M-K11c', run: n, steps: row.steps.map((s) => `${s.step}: ${s.count} files, +${s.added.length} ~${s.changed.length} -${s.removed.length}`) }));
  return row;
}

/**
 * M-K5b: turn 1 through the shim, turn 2 by `session/load` on the SAME cwd and home through the small client, then
 * `/usage` — is `Session total` cumulative across the load (its output equals the sum of every model call's
 * outputTokens in the session log) or the last turn's alone?
 */
export async function runUsage(n, ctx) {
  const { scratch, ownedHome, launch, rowOf, write, mkdir } = ctx;
  const runDir = join(scratch, `M-K5b-usage-${n}`);
  mkdir(runDir);
  const home = ownedHome(runDir);
  const workdir = join(runDir, 'work');
  write(join(workdir, 'notes.txt'), 'nothing here\n');
  const t1 = { workdir, home: home.path, owned: true, ...OK };
  write(join(runDir, 'spec.json'), JSON.stringify({ cell: 'M-K5b', arm: 'two turns', n, spec: t1 }, null, 2));
  await launch(runDir, '-t1', t1);
  const r1 = rowOf(runDir, 'M-K5b', 'turn 1 (shim)', n, t1, '-t1');
  const log1 = outputTokens(readSessionLog(r1));
  const t2 = { workdir, home: home.path, owned: true, kind: 'acp-load', prompt: 'Reply with exactly: OK2', followUp: '/usage', expect: { ok2: 'OK2' } };
  write(join(runDir, 'spec-t2.json'), JSON.stringify({ spec: t2 }, null, 2));
  if (r1.sessionId) await launch(runDir, '-t2', t2, r1.sessionId);
  const r2 = r1.sessionId ? rowOf(runDir, 'M-K5b', 'turn 2 (session/load, small client)', n, t2, '-t2') : null;
  const log2 = r2 ? outputTokens(readSessionLog(r2)) : [];
  const usage = parseUsageLine(r2?.answer2 ?? '');
  const sum = log2.reduce((a, b) => a + b, 0);
  const verdict = !usage ? 'no usage line' : usage.output === sum ? 'cumulative (output = every call in the log)' : usage.output === log2.at(-1) ? 'per turn (output = the last call only)' : `neither (${usage.output} against log ${log2.join('+')})`;
  const row = { cell: 'M-K5b', arm: 'two turns (session/load) + /usage', run: n, turn1: r1, turn2: r2, sameSession: r2 ? r2.sessionId === r1.sessionId : null,
    outputTokensAfterTurn1: log1, outputTokensAfterTurn2: log2, usage, usageLine: r2?.answer2 ?? '', verdict, model: modelAlias(r2 ? readSessionLog(r2) : '') };
  console.log(JSON.stringify({ cell: 'M-K5b', run: n, exit1: r1.exit, same: row.sameSession, log1, log2, usage, verdict }));
  return row;
}

/** The session log the probe saved for a row (`session-log<tag>.txt`), or ''. */
function readSessionLog(row) {
  const p = row.sessionLogPath;
  return p && existsSync(p) ? readFileSync(p, 'utf8') : '';
}

/** The names of a folder's entries, for a row — '' when absent. */
export const namesIn = (dir) => (existsSync(dir) ? readdirSync(dir).join(' ') : '');

/** Whether an owned home holds `updates/` and what — M-K13's column. */
export const updatesIn = (home) => files(join(home, 'updates')).map((f) => fwd(f)).join(' ') || 'absent';
