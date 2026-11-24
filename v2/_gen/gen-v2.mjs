// Writes the coai v2 feature folder (YAML contract) from one spec. Usage: node v2/_gen/gen-v2.mjs
// It writes the folder this file sits in (v2/) and takes no argument: a path from the command line would be a path anywhere on the disk.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));

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
