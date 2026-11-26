// usage: node render-prompt.mjs <repo-with-coai-template> <task.yaml> <out-file>
// Renders the coder's turn-0 prompt the way CoderPrompt.Build does (one pass over {{name}}), from the shipped template and schema.
import fs from 'node:fs'; import path from 'node:path'; import { createRequire } from 'node:module';
const [tool, taskFile, out] = process.argv.slice(2);
const require = createRequire(path.join(tool, '.agents/conventions/package.json'));
const YAML = require('yaml');
const tpl = fs.readFileSync(path.join(tool, 'src_mcp/src/Roles/prompts/coder.md'), 'utf8');
const schema = fs.readFileSync(path.join(tool, 'shared/coder-report.schema.json'), 'utf8').trimEnd();
const task = YAML.parse(fs.readFileSync(taskFile, 'utf8'));
const taskRel = process.argv[5] ?? path.relative(process.argv[6] ?? '.', taskFile).split(path.sep).join('/');
const v = {
  taskPath: taskRel, taskId: task.id, title: task.title, kind: task.kind,
  files: task.files.length === 0 ? '- (the task names no file: find the right ones in the repository)' : task.files.map(f => `- \`${f}\``).join('\n'),
  dod: task.dod.map(d => `- ${d.id}: ${d.item}`).join('\n'), schema,
};
const text = tpl.replace(/\{\{([A-Za-z]+)\}\}/g, (m, n) => { if (!(n in v)) throw new Error('unfilled ' + n); return v[n]; });
fs.writeFileSync(out, text); console.log(out, text.length, 'chars');
