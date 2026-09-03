/**
 * Would a SIZE rule have split the right plans? The corpus is its own answer key: one plan in it was
 * actually split into epics (`connect_other_ais` → epic_01..06), and the rest shipped whole.
 */
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const rows = [];
for (const root of ['research', 'todo']) {
  if (!existsSync(root)) continue;
  for (const name of readdirSync(root).filter((f) => f.startsWith('PLAN_') && f.endsWith('.md'))) {
    const text = readFileSync(join(root, name), 'utf8');
    const lines = text.split(/\r?\n/);
    const buildStart = lines.findIndex((l) => /^##\s+Build order/i.test(l));
    const buildEnd = lines.findIndex((l, i) => i > buildStart && /^##\s/.test(l));
    const steps = buildStart === -1
      ? 0
      : lines.slice(buildStart, buildEnd === -1 ? lines.length : buildEnd).filter((l) => /^\s*\d+\.\s/.test(l)).length;
    const files = new Set([...text.matchAll(/[\w./-]+\.(cs|ts|razor|json|yml|mjs)\b/g)].map((m) => m[0].split('/').pop()));
    const areas = new Set(
      [...text.matchAll(/\b(src_mcp|src_vs_code|\.github|research|prompts|tools)\b/g)].map((m) => m[1]),
    );
    rows.push({ name: name.replace(/^PLAN_|\.md$/g, ''), lines: lines.length, steps, files: files.size, areas: areas.size });
  }
}

// The proposed rule, in one place so it can be argued with.
function verdict(r) {
  const big = r.lines > 300 || r.files >= 12 || r.areas >= 3;
  const long = r.steps >= 6;
  if (big && long) return 'epics';
  if (r.steps >= 4 || r.lines > 100) return 'stories';
  return 'as it is';
}

rows.sort((a, b) => b.lines - a.lines);
console.log('| plan | lines | steps | files | areas | verdict |');
console.log('|---|---|---|---|---|---|');
for (const r of rows) {
  console.log(`| ${r.name} | ${r.lines} | ${r.steps} | ${r.files} | ${r.areas} | **${verdict(r)}** |`);
}
const counts = rows.reduce((acc, r) => ({ ...acc, [verdict(r)]: (acc[verdict(r)] ?? 0) + 1 }), {});
console.log(`\n${JSON.stringify(counts)}`);
