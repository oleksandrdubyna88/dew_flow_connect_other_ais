/**
 * Every plan this repository has, measured: how long, and how much work it actually asks for.
 *
 * The question is whether SIZE can decide "split this into epics" — so the metrics are the ones a
 * server could compute without reading the plan: lines, words, the numbered statements in
 * "What must be true", the steps in "Build order", and the distinct files the plan names.
 */
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const roots = ['research', 'todo'];
const rows = [];

for (const root of roots) {
  if (!existsSync(root)) continue;
  for (const name of readdirSync(root).filter((f) => f.startsWith('PLAN_') && f.endsWith('.md'))) {
    const text = readFileSync(join(root, name), 'utf8');
    const lines = text.split(/\r?\n/);
    const words = text.split(/\s+/).filter(Boolean).length;
    const must = lines.filter((l) => /^\s*\d+\.\s+\*\*/.test(l)).length;
    const buildStart = lines.findIndex((l) => /^##\s+Build order/i.test(l));
    const buildEnd = lines.findIndex((l, i) => i > buildStart && /^##\s/.test(l));
    const steps = buildStart === -1
      ? 0
      : lines.slice(buildStart, buildEnd === -1 ? lines.length : buildEnd)
          .filter((l) => /^\s*\d+\.\s/.test(l)).length;
    const files = new Set(
      [...text.matchAll(/[\w./-]+\.(cs|ts|razor|json|yml|md|mjs)\b/g)].map((m) => m[0].split('/').pop()),
    );
    const status = (lines.find((l) => l.includes('Status:')) ?? '').includes('IMPLEMENTED')
      ? 'implemented'
      : 'open';
    rows.push({ name, root, lines: lines.length, words, must, steps, files: files.size, status });
  }
}

rows.sort((a, b) => b.lines - a.lines);
console.log('| plan | where | lines | words | musts | steps | files | status |');
console.log('|---|---|---|---|---|---|---|---|');
for (const r of rows) {
  console.log(
    `| ${r.name.replace(/^PLAN_|\.md$/g, '')} | ${r.root} | ${r.lines} | ${r.words} | ${r.must} | ${r.steps} | ${r.files} | ${r.status} |`,
  );
}

const median = (xs) => [...xs].sort((a, b) => a - b)[Math.floor(xs.length / 2)];
console.log(`\nplans: ${rows.length}`);
console.log(`lines  — median ${median(rows.map((r) => r.lines))}, max ${Math.max(...rows.map((r) => r.lines))}`);
console.log(`steps  — median ${median(rows.map((r) => r.steps))}, max ${Math.max(...rows.map((r) => r.steps))}`);
console.log(`files  — median ${median(rows.map((r) => r.files))}, max ${Math.max(...rows.map((r) => r.files))}`);
console.log(`over 300 lines: ${rows.filter((r) => r.lines > 300).length}`);
console.log(`over 100 lines: ${rows.filter((r) => r.lines > 100).length}`);
console.log(`over 6 build steps: ${rows.filter((r) => r.steps > 6).length}`);
