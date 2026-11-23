// Writes the coai v2 feature folder (YAML contract) from one spec. Usage: node gen-v2.mjs <v2-dir>
import { mkdirSync, writeFileSync } from 'node:fs';
import { isAbsolute, join, relative, resolve } from 'node:path';

// The folder is a path under the folder this is run from — a path anywhere else is refused, so a wrong argument cannot write outside the checkout.
const base = process.cwd();
const root = process.argv[2] ? resolve(base, process.argv[2]) : '';
if (!root) { console.error('usage: gen-v2.mjs <v2-dir>'); process.exit(2); }
const inside = relative(base, root);
if (inside.startsWith('..') || isAbsolute(inside)) { console.error(`gen-v2.mjs: ${process.argv[2]} is not under ${base}`); process.exit(2); }

// Minimal YAML emitter: maps, lists, strings (always quoted when needed), numbers, booleans.
const q = (s) => (/^[A-Za-z0-9_.\/-]+$/.test(s) && !/^(true|false|null|yes|no|\d.*)$/i.test(s) ? s : JSON.stringify(s));
function y(v, ind = '') {
  if (Array.isArray(v)) {
    if (v.length === 0) return ' []';
    return v.map((x) => (typeof x === 'object' && x !== null
      ? `\n${ind}- ${y(x, ind + '  ').trimStart()}`
      : `\n${ind}- ${scalar(x)}`)).join('');
  }
  if (typeof v === 'object' && v !== null) {
    return Object.entries(v).map(([k, x]) => {
      if (typeof x === 'object' && x !== null) return `\n${ind}${k}:${y(x, ind + '  ')}`;
      return `\n${ind}${k}: ${scalar(x)}`;
    }).join('');
  }
  return ' ' + scalar(v);
}
function scalar(x) { return typeof x === 'string' ? q(x) : String(x); }
function write(dir, name, obj) {
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, name), `# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)${y(obj)}\n`);
}

const spec = JSON.parse(await (await import('node:fs/promises')).readFile(new URL('./v2-spec.json', import.meta.url), 'utf8'));

write(root, 'feature.yaml', spec.feature);
for (const ep of spec.epochs) {
  const epDir = join(root, ep.dir);
  write(epDir, 'epoch.yaml', ep.yaml);
  for (const epic of ep.epics) {
    const eDir = join(epDir, epic.dir);
    write(eDir, 'epic.yaml', epic.yaml);
    for (const st of epic.stories ?? []) {
      const sDir = join(eDir, 'stories', st.dir);
      write(sDir, 'story.yaml', st.yaml);
      for (const t of st.tasks ?? []) write(join(sDir, 'tasks', t.id), 'task.yaml', t);
    }
  }
}
console.log('written');
