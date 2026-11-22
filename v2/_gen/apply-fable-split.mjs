// One-time edit of v2-spec.json: applies the Fable split decision of 2026-11-22 (5 epochs, MVP-first epics).
// Usage: node v2/_gen/apply-fable-split.mjs   (from the repository root), then regenerate with gen-v2.mjs.
import { readFileSync, writeFileSync } from 'node:fs';

const p = 'v2/_gen/v2-spec.json';
const s = JSON.parse(readFileSync(p, 'utf8'));
const ep = (id) => s.epochs.find((e) => e.dir === id);
const epic = (e, id) => ep(e).epics.find((x) => x.dir === id);
const T = (id, title, kind, complexity, dependsOn, parallel, files, dod) =>
  ({ id, title, kind, complexity, dependsOn, parallel, files, dod: dod.map((item, i) => ({ id: `d${i + 1}`, item })) });
const st = (dir, title, dependsOn, tasks) => ({ dir, yaml: { id: dir, title, dependsOn }, tasks });
const E = (dir, title, o) => ({ dir, yaml: { id: dir, title, ...o } });
const S = (e, d) => e.stories.find((x) => x.dir === d);

// ---- epoch 1
const oe1 = epic('epoch-1', 'e1-gate-simplify');
const oe2 = epic('epoch-1', 'e2-thin-shell');
const oe5 = epic('epoch-1', 'e5-release-2-0');
const s1 = S(oe1, 's1-verdicts-and-holds'), s2 = S(oe1, 's2-remove-modes'), s3 = S(oe1, 's3-renames');
const s4 = S(oe1, 's4-migrate-v2'), s5 = S(oe1, 's5-conventions-out');
s4.yaml.dependsOn = ['s1-verdicts-and-holds']; s2.yaml.dependsOn = []; s5.yaml.dependsOn = [];
const e2s1 = S(oe2, 's1-shell-core'), e2s2 = S(oe2, 's2-measure'), e2s3 = S(oe2, 's3-sidebar');
const e2s4 = S(oe2, 's4-chat-carried'), e2s5 = S(oe2, 's5-install-module');
const bandTask = e2s3.tasks.shift();
e2s3.tasks[0].dependsOn = []; e2s3.yaml.dependsOn = [];

const live = st('s6-live-proof', 'Live proof: real plan→code gates and migration on copies of real data', ['s1-verdicts-and-holds', 's4-migrate-v2'], [
  T('t1-live-gates', 'Run 3 real plan→code gates on the E1 binary; record 0 person waits and only the four outcomes', 'measure', 4, [], false, ['research/RESULTS_v2_gate_core.md'], ['3 real gates recorded with verdicts and reviewer counts']),
  T('t2-migrate-copies', '--migrate-v2 on a COPY of each real data folder: dry-run counts = apply counts; second run 0 changes', 'measure', 5, [], true, ['research/RESULTS_v2_gate_core.md'], ['counts recorded per data folder']),
  T('t3-outcome-share', 'Share of good_enough/not_reviewed vs the v1 call_human share from RoundsDb', 'measure', 3, ['t1-live-gates'], false, ['research/RESULTS_v2_gate_core.md'], ['both shares recorded with the query used'])]);
oe1.stories = [s1, s4, live];
oe1.yaml.title = 'Gate core: four outcomes, v1 data migrated, live proof';
oe1.yaml.doneWhen = [
  'only proceed, revise, good_enough and not_reviewed come out of a round',
  'the deleted classes are gone and the MCP test executable is green',
  '--migrate-v2 converts every released fixture exactly once; an interrupted run ends equal to one clean run',
  '3 real plan→code gates end with 0 person waits; migration on copies of real data: dry-run = apply, second run 0; outcome shares recorded'];

const joint = st('s6-joint-smoke', 'Joint smoke: shell VSIX + E1 binary', ['s2-measure'], [
  T('t1-joint-round', 'Install the shell VSIX with the E1 binary; one real round shown; no hold/card path runs', 'measure', 4, [], false, ['research/RESULTS_v2_start.md'], ['round shown in the sidebar; no card or hold code reached']),
  T('t2-settings-mirror', 'Settings mirror under lazy activation: a window that never opens coai still gives the server current settings, or the gap is closed', 'test', 5, [], true, ['src_vs_code/src/test/host'], ['result recorded; a gap is fixed or the server reads settings without the mirror'])]);
oe2.stories = [e2s1, e2s2, joint];
oe2.yaml.doneWhen = [
  'no onStartupFinished; activate() only registers (entryPurity test)',
  'entry firewall checks inputs and output imports; byte budgets green',
  'M1 ≤ 50 ms; M2 maximum of 3 fresh samples ≤ 2000 ms on Linux CI, Windows CI and Remote-WSL',
  'v1 baseline and per-step marks of the old activate() recorded',
  'joint smoke with the E1 binary passes; settings-mirror check done'];

const e3 = {
  dir: 'e3-server-cleanup-install',
  yaml: { id: 'e3-server-cleanup-install', title: 'Server cleanup, renames, conventions optional, --band-json, install, roles', half: 'server',
    dependsOn: ['e1-gate-simplify'], parallelWith: ['e4-shell-modules'],
    doneWhen: ['cons, cons_close, consask served; old names answer as aliases',
      'coai ships and enforces no conventions; a repo that mounts them still has them read',
      'roles returns main AI, ORDERED model lists per role, complexity bands, Haiku kinds, work method',
      '--install on a clean WSL box gives a working Claude + Codex (+ agy) MCP setup with the SessionStart roles hook; idempotent; NO AskUserQuestion hook',
      '--band-json answers phrases, consultations, failures'] },
  stories: [s2, s3, s5, st('s4-band-json', '--band-json one-shot', [], [bandTask]),
    st('s6-install', '--install and --setup-clients: find CLIs, write MCP config (cornerstone)', ['s3-renames'], [
      T('t1-find-clis', 'Find claude, codex, agy (ExecutablePaths); report versions', 'code', 4, [], false, ['src_mcp/src/Install'], ['absent CLIs reported, not guessed']),
      T('t2-write-config', 'Write MCP config through vendor commands (claude mcp add-json --scope user, codex mcp add); agy path measured first; backup + idempotent by command path', 'code', 7, ['t1-find-clis'], false, ['src_mcp/src/Install'], ['second run changes nothing', 'a backup exists before the first write']),
      T('t3-clean-box', 'Clean WSL box test (fresh HOME): one command → working MCP setup', 'test', 5, ['t2-write-config'], false, ['src_mcp/tests'], ['claude and codex list the coai server'])]),
    st('s7-roles-hook', 'roles tool + SessionStart hook', ['s6-install'], [
      T('t1-roles-tool', 'MCP tool roles + one-shot --roles [--format json|claude-hook]; shared/role-defaults.json with ordered model lists per role and main-AI vendor', 'code', 6, [], false, ['src_mcp/src', 'shared/role-defaults.json'], ['codex/agy rows only from measured model lists']),
      T('t2-sessionstart', 'Installer merges the SessionStart → --roles --format claude-hook hook', 'code', 4, ['t1-roles-tool'], false, ['src_mcp/src/Install'], ['a new Claude session shows the role table'])])] };

const deadOut = st('s6-dead-v1-out', 'Dead v1 watchers and controls out of the bundle', ['s4-chat-carried'], [
  T('t1-remove-watchers', 'Remove escalation/consultation/question watchers, cadence and hold controls from the extension', 'code', 5, [], false, ['src_vs_code/src'], ['no escalation or cadence control or watcher in the bundle']),
  T('t2-recheck-m2', 'Re-run the measure launch; M2 still ≤ 2000 ms', 'measure', 2, ['t1-remove-watchers'], false, ['research/RESULTS_v2_start.md'], ['new M2 recorded'])]);
const e4 = {
  dir: 'e4-shell-modules',
  yaml: { id: 'e4-shell-modules', title: 'Shell modules: sidebar, chat carried, install after ready, dead v1 out', half: 'extension', dependsOn: ['e2-thin-shell'],
    doneWhen: ['sidebar polls only while visible', 'all chat host scenarios pass', 'M2 still ≤ 2000 ms', 'no escalation or cadence control or watcher in the bundle'] },
  stories: [e2s3, e2s4, e2s5, deadOut] };
oe5.yaml.dependsOn = ['e3-server-cleanup-install', 'e4-shell-modules'];
ep('epoch-1').epics = [oe1, oe2, e3, e4, oe5];
ep('epoch-1').yaml.epics = ep('epoch-1').epics.map((x) => x.dir);

// ---- epoch 2
const o2 = ep('epoch-2');
const g = (id) => o2.epics.find((x) => x.dir === id).yaml.doneWhen;
const g1 = g('e1-hub-server'), g2 = g('e2-device-agent-vm-shared'), g3 = g('e3-update-everywhere'), g5 = g('e5-rollout');
o2.epics = [
  E('e1-hub-slice', 'Hub slice: boots alone, enrol, device document, applied, one log stream, --hub-sync', { mvp: true, half: 'both', dependsOn: [],
    doneWhen: ['a setting changed from the Windows window is reported applied by a VM user in ≤ 90 s', 'stale write = 412; a device token is refused on another device and on Team routes', ...g1] }),
  E('e2-vm-shared', 'VM shared instance', { mvp: true, half: 'server', dependsOn: [],
    doneWhen: ['UID B runs claude, codex and agy after UID A logged in, also after a token refresh by the other UID', 'two UIDs write SQLite at the same time', 'shared /var/lib/coai with group coai; existing db/WAL permissions repaired'] }),
  E('e3-device-agent-sync', 'Device agent and sync: lease, cache, single writer, outbox, sweeper, v1 → device-document migration, first sync', { half: 'both', dependsOn: ['e1-hub-slice', 'e2-vm-shared'],
    doneWhen: [...g2.filter((x) => !x.startsWith('shared /var')), 'migration: user settings AND side overlays backed up, effective values per side, idempotent, v1 keys untouched', 'the legacy settings mirror is off on a hub-managed device'] }),
  E('e4-update-rollout', 'Update everywhere and rollout to all devices', { half: 'both', dependsOn: ['e3-device-agent-sync'], doneWhen: [...g3, ...g5] }),
  E('e5-kit-devices-page', 'Kit 0.3.0 + Devices page (roles → model lists, work method)', { half: 'extension', dependsOn: ['e1-hub-slice'],
    doneWhen: ['kit 0.3.0 released with K1-K6, each with a story, fakeDom and axe test', 'Devices: tree VM → user / WSL / Windows, per-device settings and logs, enrolment lines, update everywhere with progress', '412 handled: reload, changed elsewhere, no overwrite'] })];
o2.yaml.epics = o2.epics.map((x) => x.dir);

// ---- epoch 3 (read the old lines BEFORE the list is replaced)
const o3 = ep('epoch-3');
const h = (id) => o3.epics.find((x) => x.dir === id).yaml.doneWhen;
const adapters = h('e1-toolchain-adapters'), coder = h('e2-coder-fix-loop'), sched = h('e3-work-scheduler');
const askOld = h('e4-ask-hook'), sessOld = h('e5-sessions-mod');
o3.yaml.proves = 'an external role does a task that coai routes, checks and keeps green';
o3.epics = [
  E('e1-role-jobs-ba', 'External-role jobs + router + failover + work-plan schema + BA role', { mvp: true, half: 'server', dependsOn: [],
    doneWhen: ['start_role/role_status/role_cancel; detached job with lease + generation',
      'BA writes the stories and tasks of the next real v2 epic; validate_work passes in ≤ 2 repair turns; the orchestrator changes < 20 % of the task files',
      'FakeCli refusal / limit / crash → next model; one log line and one notification per switch',
      'work-plan schema with the security flag; validate_work; workMethod limits from the device config',
      'complexity 10 or security → the important model list'] }),
  E('e2-coder-one-task', 'Coder: one task to green on this repository', { mvp: true, half: 'server', dependsOn: ['e1-role-jobs-ba'],
    doneWhen: ['coder is one role case; this repository through .coai/toolchain.yaml; claude only',
      'green in ≤ 3 fix turns; main-AI tokens and wall time below the baseline; accepted gate findings not higher',
      'task session inherits the feature plan proceed; write boundary at launch'] }),
  E('e3-roles-complete', 'Roles complete: scout, important/judge, codex + agy rows, review_plan(featureFolder)', { half: 'server', dependsOn: ['e1-role-jobs-ba'],
    doneWhen: ['scout and judge strict JSON outputs', 'codex and agy coder sessions resumed', 'review_plan(featureFolder) validates first, packs in topological order'] }),
  E('e4-adapters-coder-hardening', 'Adapters dotnet + node + coder hardening', { half: 'server', dependsOn: ['e2-coder-one-task'], doneWhen: [...adapters, ...coder.slice(1)] }),
  E('e5-work-scheduler', 'Parallel work scheduler', { half: 'server', dependsOn: ['e2-coder-one-task', 'e4-adapters-coder-hardening'],
    doneWhen: ['story 1 = two tasks in parallel; go on only if speedup ≥ 1.3× and peak RAM stays in the WSL limit', ...sched] })];
o3.yaml.epics = o3.epics.map((x) => x.dir);

// ---- epoch 4 Operator (new); old epoch 4 becomes epoch 5
const o5 = ep('epoch-4');
o5.dir = 'epoch-5'; o5.yaml.id = 'epoch-5'; o5.yaml.dependsOn = ['epoch-3'];
const o4 = { dir: 'epoch-4', yaml: { id: 'epoch-4', title: 'Operator', proves: 'the person works through coai: questions answered, sessions one click away, everything visible', dependsOn: ['epoch-1'], epics: [] },
  epics: [
    E('e1-ask-hook', 'AskUserQuestion hook (spike = story 1)', { mvp: true, half: 'server', dependsOn: [], doneWhen: askOld }),
    E('e2-sessions-creds', 'tmux sessions through the creds MCP (spike = story 1)', { mvp: true, half: 'extension', dependsOn: [], doneWhen: sessOld.filter((x) => !x.startsWith('mod:')) }),
    E('e3-claude-mod', 'Claude mod: phrases, active consults, failures', { half: 'extension', dependsOn: [], doneWhen: sessOld.filter((x) => x.startsWith('mod:')) }),
    E('e4-local-pages', 'Local pages: Prompts & Commands, Phrases, Rounds (kit 0.3.0)', { half: 'extension', dependsOn: [],
      doneWhen: ['each page tested by running its shipped bundle', 'prompt overrides and facets (gates, review, documents, cons, consask, chat, roles, coder)'] }),
    E('e5-features-page', 'Features page (needs epoch-3 e1)', { half: 'extension', dependsOn: [],
      doneWhen: ['epoch → epic → story → task tree with status, complexity, model, parallel lanes; a remote-only feature through the hub'] })] };
o4.yaml.epics = o4.epics.map((x) => x.dir);

s.epochs = [ep('epoch-1'), o2, o3, o4, o5];
s.feature.epochs = s.epochs.map((x) => x.dir);
s.feature.taskSchema = { security: 'boolean flag; true routes the task to the important role', roles: 'ordered model lists per role' };
s.feature.cornerstones = ['epoch-1/e1/s4-migrate-v2', 'epoch-1/e3/s6-install', 'epoch-2/e1 hub auth + device-document contract',
  'epoch-2/e2 shared vendor logins + file permissions', 'epoch-2/e3 first sync + settings migration, single writer', 'epoch-2/e4 binary swap',
  'epoch-3/e1 router + failover + job lease', 'epoch-3/e1 work-plan schema', 'epoch-3/e2 plan binding + envelope/report schema',
  'epoch-3/e5 host slots + merge on candidate SHA'];
writeFileSync(p, JSON.stringify(s, null, 2));
console.log('spec updated');
