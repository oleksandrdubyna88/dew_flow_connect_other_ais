// Drives the settings mockup in headless Chrome over CDP: clicks through the flows, asserts, screenshots.
// Usage: node new_design/check.mjs [out-dir]   (CHROME=<path> to use another Chromium). Node 22+.
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';

const OUT = process.argv[2] || join(tmpdir(), 'coai-mockup-check');
const URL = pathToFileURL(join(dirname(fileURLToPath(import.meta.url)), 'index.html')).href;
const CHROME = process.env.CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PROFILE = `${OUT}/cdp-prof`;
mkdirSync(OUT, { recursive: true });
rmSync(PROFILE, { recursive: true, force: true });
mkdirSync(PROFILE, { recursive: true });

const chrome = spawn(CHROME, ['--headless=new', '--disable-gpu', '--hide-scrollbars', '--remote-debugging-port=9333',
  `--user-data-dir=${PROFILE}`, '--window-size=1920,1300', 'about:blank'], { stdio: 'ignore' });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let ws; let seq = 0; const pending = new Map(); const errors = [];

async function connect() {
  for (let n = 0; n < 50; n++) {
    try {
      const list = await (await fetch('http://127.0.0.1:9333/json')).json();
      const page = list.find((t) => t.type === 'page');
      if (page) return page.webSocketDebuggerUrl;
    } catch { /* not up yet */ }
    await sleep(200);
  }
  throw new Error('chrome did not come up');
}

function send(method, params = {}) {
  const id = ++seq;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((res, rej) => pending.set(id, { res, rej }));
}

async function js(expr) {
  const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(`${expr}\n${JSON.stringify(r.exceptionDetails)}`);
  return r.result.value;
}

async function shot(name, w = 1920, h = 1300) {
  await send('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: false });
  await sleep(250);
  const { data } = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(`${OUT}/${name}.png`, Buffer.from(data, 'base64'));
}

const checks = [];
const check = (name, ok, detail = '') => checks.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ` — ${detail}` : ''}`);

try {
  ws = new WebSocket(await connect());
  await new Promise((r) => ws.addEventListener('open', r));
  ws.addEventListener('message', (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.rej(new Error(m.error.message)) : p.res(m.result); }
    if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails.exception?.description);
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errors.push(JSON.stringify(m.params.args));
  });
  await send('Runtime.enable');
  await send('Page.enable');
  await send('Page.navigate', { url: URL });
  await sleep(800);
  await js('localStorage.clear(); location.reload()');
  await sleep(800);

  const cards0 = await js('document.querySelectorAll(".card").length');
  check('ten demo instances render', cards0 === 10, String(cards0));
  const cols = await js('getComputedStyle(document.querySelector(".cards")).gridTemplateColumns.split(" ").length');
  check('two columns at 1920', cols === 2, String(cols));
  await shot('01-models-wide');

  // two cards in one row: same height, and every section starts on the same line
  const misaligned = await js(`(() => {
    const rows = new Map();
    document.querySelectorAll('.card').forEach((c) => { const k = Math.round(c.getBoundingClientRect().top); rows.set(k, [...(rows.get(k) || []), c]); });
    const lines = (c) => [...c.children].map((x) => Math.round(x.getBoundingClientRect().top)).concat(Math.round(c.getBoundingClientRect().bottom));
    return [...rows.values()].filter((p) => p.length === 2 && JSON.stringify(lines(p[0])) !== JSON.stringify(lines(p[1])))
      .map((p) => p.map((c) => c.dataset.id).join('|')).join(', '); })()`);
  check('cards side by side share their height and section lines', misaligned === '', misaligned || 'all rows aligned');

  // effort and vendor rows narrow together
  await js('document.querySelector("[data-filter-kind=effort][data-value=high]").click()');
  await sleep(150);
  const high = await js('[...document.querySelectorAll(".card")].map(c=>c.dataset.id).join(",")');
  check('the effort row filters by effort', high === 'codex-2,claude-2,qwen-2', high);
  await js('document.querySelector("[data-filter-kind=vendor][data-value=dashscope]").click()');
  await sleep(150);
  const both = await js('[...document.querySelectorAll(".card")].map(c=>c.dataset.id).join(",")');
  check('the vendor row narrows what the effort row shows', both === 'qwen-2', both);
  await shot('01b-filters', 1920, 900);
  await js('document.querySelector("[data-action=clear-filters]").click()');
  await sleep(150);

  // "new" lasts a week
  const tagsNow = await js('document.querySelectorAll(".tag-new").length');
  await js(`(() => { const d = document.querySelector('#days'); d.value = '7'; d.dispatchEvent(new Event('change')); })()`);
  await sleep(150);
  const tagsLater = await js('document.querySelectorAll(".tag-new").length');
  check('"new" shows in the first week and is gone on day 7', tagsNow > 0 && tagsLater === 0, `${tagsNow} → ${tagsLater}`);
  await js(`(() => { const d = document.querySelector('#days'); d.value = '0'; d.dispatchEvent(new Event('change')); })()`);

  // brightness moves the text colour, size moves the font
  const before = await js('getComputedStyle(document.querySelector(".lead")).color');
  await js('document.querySelector("[data-tone=\\"1\\"]").click(); document.querySelector("[data-tone=\\"1\\"]").click()');
  const after = await js('getComputedStyle(document.querySelector(".lead")).color');
  check('brightness changes the text colour', before !== after, `${before} → ${after}`);
  await js('document.querySelector("[data-tone=\\"-1\\"]").click(); document.querySelector("[data-tone=\\"-1\\"]").click()');

  // duplicate GLM 5.3 · low -> qwen-3, placed right after it
  await js('document.querySelector("#card-qwen [data-action=duplicate]").click()');
  await sleep(300);
  const dupId = await js('[...document.querySelectorAll(".card")].map(c=>c.dataset.id).join(",")');
  check('duplicate gets the next free id, after its source', dupId.includes('qwen,qwen-3,qwen-2'), dupId);

  // change effort on the copy and write a custom system prompt
  await js(`(() => { const c = document.querySelector('#card-qwen-3');
    const s = c.querySelector('[data-field=effort]'); s.value = 'max'; s.dispatchEvent(new Event('change', {bubbles:true}));
    const m = document.querySelector('#card-qwen-3 [data-field=systemPromptMode]'); m.value = 'custom'; m.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(200);
  await js(`(() => { const t = document.querySelector('#card-qwen-3 textarea'); t.value = 'You review security only. Answer in English.'; t.dispatchEvent(new Event('input', {bubbles:true})); })()`);
  const stored = await js('JSON.parse(localStorage.getItem("coai-settings-mockup-v1")).instances.find(i=>i.id==="qwen-3")');
  check('effort and system prompt are kept per instance', stored.effort === 'max' && stored.systemPrompt.text.startsWith('You review'), `${stored.effort} / ${stored.systemPrompt.mode}`);
  await js('document.querySelector("#card-qwen-3").scrollIntoView({block:"start"})');
  await shot('02-duplicate-custom-prompt');

  // a disabled feature cannot be ticked
  const bugzDisabled = await js('document.querySelector("#card-codex [data-feature=bugz]").disabled');
  check('Bugz is not offered on a CLI instance', bugzDisabled === true);

  // feature filter chip
  await js('document.querySelector("[data-filter-kind=feature][data-value=consultant]").click()');
  await sleep(200);
  const consultCards = await js('[...document.querySelectorAll(".card")].map(c=>c.dataset.id).join(",")');
  check('the Consultant chip filters to consultant instances', consultCards === 'codex-2,claude-2,qwen-2', consultCards);
  await js('document.querySelector("[data-filter-kind=feature][data-value=consultant]").click()');

  // add dialog: Alibaba
  await js('document.querySelector("[data-action=add]").click()');
  await sleep(200);
  await shot('03-add-pick');
  await js('document.querySelector("[data-pick=dashscope]").click()');
  await sleep(200);
  await shot('04-add-form');
  await js('document.querySelector("[data-commit]").click()');
  await sleep(600);
  const added = await js('JSON.parse(localStorage.getItem("coai-settings-mockup-v1")).instances.map(i=>i.id).join(",")');
  check('add appends qwen-4', added.endsWith('qwen-4'), added);

  // the question consultant flags a Codex row (checked before codex-2 is removed below)
  await js('document.querySelector("[data-tab=consultants]").click()');
  await sleep(150);
  await js('document.querySelector("[data-sub=qconsult]").click()');
  await sleep(150);
  const flagged = await js('document.querySelector("[data-row=row-2]").innerText.includes("Can read this machine")');
  check('a Codex row is flagged as able to read this machine', flagged);
  await js('document.querySelector("[data-tab=models]").click()');
  await sleep(150);

  // remove the consultant for Claude Code: callers referencing it are cleared
  await js('document.querySelector("#card-codex-2 [data-action=remove]").click()');
  await sleep(200);
  await shot('05-remove-confirm', 1920, 900);
  await js('document.querySelector("[data-confirm-remove]").click()');
  await sleep(300);
  const callers = await js('JSON.parse(localStorage.getItem("coai-settings-mockup-v1")).consultants');
  check('removing an instance clears the callers that used it', callers.claude === '', JSON.stringify(callers));

  // consultants tab: overlap + nobody notes (the tab remembers its last sub-tab, so pick it)
  await js('document.querySelector("[data-tab=consultants]").click()');
  await sleep(200);
  await js('document.querySelector("[data-sub=consultant]").click()');
  await sleep(300);
  await js(`(() => { const s = document.querySelector('[data-caller=claude]'); s.value = 'claude-2'; s.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(300);
  const overlap = await js('document.querySelector("#pane").innerText.includes("Same vendor as the caller (Anthropic)")');
  check('same-vendor consultant is shown, not refused', overlap);
  await shot('06-consultant', 1920, 1000);

  // Setup: four sub-tabs; every panel row is one height
  const panelsAligned = `(() => {
    const rows = new Map();
    document.querySelectorAll('.plan-grid > .panel').forEach((p) => { const k = Math.round(p.getBoundingClientRect().top); rows.set(k, [...(rows.get(k) || []), Math.round(p.getBoundingClientRect().height)]); });
    return [...rows.values()].every((h) => new Set(h).size === 1); })()`;
  await js('document.querySelector("[data-tab=setup]").click()');
  await sleep(250);
  const keysText = await js('document.querySelector("#pane").innerText');
  check('Vendor keys lists every key a model uses, and the vault answer', keysText.includes('DASHSCOPE_TOKEN_PLAN') && keysText.includes('found'));
  check('Vendor keys panels side by side are one height', await js(panelsAligned));
  await shot('09-setup-keys', 1920, 1100);
  await js('document.querySelector("[data-sub=team]").click()');
  await sleep(200);
  await js('document.querySelector("[data-setup=add-server]").click()');
  await sleep(150);
  await js(`(() => { for (const [f, v] of [['name', 'Lab'], ['url', 'https://coai-lab.example.com']]) {
    const el = document.querySelector('[data-add-server=' + f + ']'); el.value = v; el.dispatchEvent(new Event('input', {bubbles:true})); } })()`);
  await js('document.querySelector("[data-setup=add-server-commit]").click()');
  await sleep(1100);
  const servers = await js('JSON.parse(localStorage.getItem("coai-settings-mockup-v1")).teamServers.map(s=>s.name).join(",")');
  check('a Team server can be added', servers === 'Company,Lab', servers);
  check('Team server panels side by side are one height', await js(panelsAligned));
  await shot('10-setup-team', 1920, 1300);
  await js('document.querySelector("[data-sub=mcp]").click()');
  await sleep(200);
  check('MCP server panels side by side are one height', await js(panelsAligned));
  await shot('11-setup-mcp', 1920, 1100);

  // the data folder: a move refuses the current folder, then runs every step and switches; a change applies
  const dataDir = () => js('JSON.parse(localStorage.getItem("coai-settings-mockup-v1")).setup.data');
  const setTarget = (v) => js(`(() => { const i = document.querySelector('[data-flow=target]'); i.value = ${JSON.stringify(v)}; i.dispatchEvent(new Event('input', {bubbles:true})); })()`);
  await js('document.querySelector("[data-setup=move-dir]").click()');
  await sleep(150);
  await setTarget('E:\\coai-data');
  await js('document.querySelector("[data-setup=move-start]").click()');
  await sleep(150);
  const needsTick = await dataDir();
  check('a move does not start until the server is stopped', needsTick.directory !== 'E:\\coai-data');
  await js(`(() => { const b = document.querySelector('[data-flow=stopped]'); b.checked = true; b.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(150);
  await setTarget('D:\\coai-data');
  await js('document.querySelector("[data-setup=move-start]").click()');
  await sleep(150);
  const refused = await js('document.querySelector(".flow").innerText.includes("already uses")');
  check('a move to the folder in use is refused, saying why', refused);
  await setTarget('E:\\coai-data');
  await js('document.querySelector("[data-setup=move-start]").click()');
  await sleep(1200);
  await shot('11b-setup-move-running', 1920, 1100);
  await sleep(3200);
  const moved = await dataDir();
  const allDone = await js('[...document.querySelectorAll(".move-steps .state")].every((s) => s.classList.contains("ok"))');
  check('a move runs every step and switches the folder', moved.directory === 'E:\\coai-data' && allDone, moved.directory);
  check('a finished move is remembered, so the old folder can be deleted after a reload', Boolean(moved.movedFrom), String(moved.movedFrom));
  await shot('11c-setup-move-done', 1920, 1100);
  await js('document.querySelector("[data-setup=flow-cancel]").click()');
  await sleep(150);
  await js('document.querySelector("[data-setup=change-dir]").click()');
  await sleep(150);
  await js(`(() => { const r = document.querySelector('[data-flow=pick][value=choose]'); r.checked = true; r.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(150);
  await setTarget('F:\\shared\\coai');
  await js('document.querySelector("[data-setup=change-apply]").click()');
  await sleep(200);
  const offered = await js('Boolean(document.querySelector("[data-setup=change-continue]"))');
  check('a folder that holds a history is offered to continue, never refused', offered);
  await shot('11d-setup-change', 1920, 1100);
  await js('document.querySelector("[data-setup=change-continue]").click()');
  await sleep(200);
  const changed = await dataDir();
  check('a change applies the folder, for this side only', changed.directory === 'F:\\shared\\coai' && changed.source === 'this side', `${changed.directory} / ${changed.source}`);
  await js('document.querySelector("[data-sub=side]").click()');
  await sleep(200);
  await shot('12-setup-side', 1920, 800);

  // every remaining page: blocks side by side are one height
  const rowsAligned = `(() => {
    const groups = [...document.querySelectorAll('.plan-grid, .prompt-cards')];
    return groups.every((g) => {
      const rows = new Map();
      [...g.children].forEach((p) => { const k = Math.round(p.getBoundingClientRect().top); rows.set(k, [...(rows.get(k) || []), Math.round(p.getBoundingClientRect().height)]); });
      return [...rows.values()].every((h) => new Set(h).size === 1);
    }); })()`;
  const pages = [['reviews', 'stages'], ['reviews', 'roles'], ['reviews', 'prompts'], ['reviews', 'gate'], ['reviews', 'limits'],
    ['consultants', 'consultant'], ['consultants', 'qconsult'], ['security', ''], ['chat', '']];
  for (const [tab, sub] of pages) {
    await js(`document.querySelector("[data-tab=${tab}]").click()`);
    await sleep(150);
    if (sub) { await js(`document.querySelector("[data-sub=${sub}]").click()`); await sleep(150); }
    const name = `${tab}${sub ? `-${sub}` : ''}`;
    check(`${name}: blocks side by side are one height`, await js(rowsAligned));
    await shot(`p-${name}`, 1920, 1300);
  }

  // Reviews: a role switched off greys its numbers and leaves the prompts page
  await js('document.querySelector("[data-tab=reviews]").click()');
  await js('document.querySelector("[data-sub=stages]").click()');
  await sleep(150);
  await js(`(() => { const b = document.querySelector('[data-rv-role=Architecture][data-k=enabled]'); b.checked = false; b.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(150);
  const archOff = await js('document.querySelector("[data-rv-role=Architecture][data-k=rounds]").disabled');
  check('a role switched off cannot take rounds', archOff === true);
  await js(`(() => { const n = document.querySelector('[data-rv-role=SecurityReliability][data-k=rounds]'); n.value = '3'; n.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await js('document.querySelector("[data-sub=prompts]").click()');
  await sleep(150);
  const secPicks = await js('document.querySelectorAll("[data-rv-pick=SecurityReliability]").length');
  check('three rounds give three prompt pickers', secPicks === 3, String(secPicks));

  // Roles & prompts: replacing shipped text marks it edited; an added prompt reaches the round picker;
  // one switch per role — off here is off on Stages
  await js('document.querySelector("[data-sub=roles]").click()');
  await sleep(150);
  await js(`(() => { const t = document.querySelector('[data-role=SecurityReliability] [data-prompt=sec-blast-radius] textarea');
    t.value = 'Count what breaks when this fails.'; t.dispatchEvent(new Event('input', {bubbles:true})); t.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(150);
  const edited = await js(`(() => { const p = document.querySelector('[data-role=SecurityReliability] [data-prompt=sec-blast-radius]');
    return p.classList.contains('edited') && !p.querySelector('[data-rv-action=restore-prompt]').disabled; })()`);
  check('replacing a shipped prompt\'s text marks it edited and offers Restore', edited);
  await shot('p-reviews-roles-edited', 1920, 1300);
  // Architecture was switched off by an earlier step; a role that is off shows no picker, so turn it back on
  await js(`(() => { const b = document.querySelector('[data-role=Architecture] [data-rv-role=Architecture][data-k=enabled]'); b.checked = true; b.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(150);
  const pickHasMine = await js(`(() => { document.querySelector('[data-sub=prompts]').click();
    return [...document.querySelectorAll('[data-rv-pick=Architecture] option')].some((o) => o.value === 'arch-migration'); })()`);
  check('a prompt you added is offered in the round picker', pickHasMine);
  await js('document.querySelector("[data-sub=roles]").click()');
  await sleep(150);
  await js(`(() => { const b = document.querySelector('[data-role=Conventions] [data-rv-role=Conventions][data-k=enabled]'); b.checked = false; b.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await js('document.querySelector("[data-sub=stages]").click()');
  await sleep(150);
  const sameSwitch = await js('document.querySelector("[data-rv-role=Conventions][data-k=enabled]").checked === false');
  check('a role has one switch: off on Roles & prompts is off on Stages', sameSwitch);

  // Question consultant: a disk prompt cannot run on an API-key model; Codex is flagged
  await js('document.querySelector("[data-tab=consultants]").click()');
  await js('document.querySelector("[data-sub=qconsult]").click()');
  await sleep(150);
  const diskOnApi = await js('document.querySelector("[data-row=row-3] [data-aq-row=prompt] option[value=question-disk]").disabled');
  check('a disk prompt is refused for an API-key model', diskOnApi === true);
  const orphan = await js('document.querySelector("[data-row=row-2]").innerText.includes("pick a model and a prompt")');
  check('a row whose model was removed asks for another, and cannot run', orphan);

  // Consultant: Check runs and reports
  await js('document.querySelector("[data-sub=consultant]").click()');
  await sleep(150);
  await js('document.querySelector("[data-ac-check=codex]").click()');
  await sleep(150);
  await js('document.querySelector("[data-confirm-generic]").click()');
  await sleep(1500);
  const checkedLine = await js('document.querySelector("#pane").innerText.includes("answered in 9 s")');
  check('Check reports the consultant\'s answer', checkedLine);

  // Security lane: general first; a tick makes a pair; a custom prompt without text says so
  await js('document.querySelector("[data-tab=security]").click()');
  await sleep(150);
  const first = await js('document.querySelector(".prompt-cards .pcard").dataset.sp');
  check('the general prompt is first', first === 'redteam-general', first);
  const pairsBefore = await js('document.querySelectorAll("[data-pair]").length');
  await js(`(() => { const b = document.querySelector('[data-sp=redteam-general] [data-sl-tick=codex]'); b.checked = true; b.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(150);
  const pairsAfter = await js('document.querySelectorAll("[data-pair]").length');
  check('ticking a model on a card adds a pair', pairsAfter === pairsBefore + 1, `${pairsBefore} → ${pairsAfter}`);
  // the prompt text is edited in place: shipped → edited → Restore → shipped; a custom prompt loses its red line
  const typeText = (id, text) => js(`(() => { const c = document.querySelector('[data-sp=${id}]'); c.querySelector('details[data-text-open]').open = true;
    const t = c.querySelector('textarea[data-sl-text]'); t.value = ${JSON.stringify(text)};
    t.dispatchEvent(new Event('input', {bubbles:true})); t.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await typeText('redteam-general', 'Look for anything an attacker could reach from outside.');
  await sleep(150);
  const generalEdited = await js('document.querySelector("[data-sp=redteam-general]").classList.contains("edited")');
  await js('document.querySelector("[data-sp=redteam-general] [data-sl-action=restore]").click()');
  await sleep(150);
  await js('document.querySelector("[data-confirm-generic]").click()');
  await sleep(150);
  const generalBack = await js('document.querySelector("[data-sp=redteam-general]").classList.contains("shipped")');
  check('a shipped prompt\'s text is edited in place, and Restore puts it back', generalEdited && generalBack, `${generalEdited} → ${generalBack}`);
  const redBefore = await js('document.querySelector("[data-sp=redteam-billing]").innerText.includes("No prompt text yet")');
  await typeText('redteam-billing', 'Find every way two payments can be taken for one order.');
  await sleep(150);
  const redAfter = await js('document.querySelector("[data-sp=redteam-billing]").innerText.includes("No prompt text yet")');
  check('writing a custom prompt\'s text clears its warning', redBefore && !redAfter);
  await js('document.querySelector("[data-sp=redteam-billing]").scrollIntoView({block:"center"})');
  await shot('p-security-text', 1920, 1100);
  // triggers: SQL in the diff starts redteam-sql; a word you add finds the signal; removing a preset's last trigger warns
  const sqlStarts = await js('document.querySelector("[data-signal=sql]").textContent.includes("redteam-sql")');
  check('the routing table says sql starts redteam-sql', sqlStarts);
  const tryIt = async (text) => {
    await js(`(() => { const t = document.querySelector('[data-sl-try]'); t.value = ${JSON.stringify(text)}; t.dispatchEvent(new Event('input', {bubbles:true})); })()`);
    return js('document.querySelector("[data-try-result]").innerText');
  };
  const sqlTry = await tryIt('var rows = db.Query<Order>("SELECT * FROM orders WHERE id = " + id);');
  check('Try it: SQL in a diff would run redteam-sql', sqlTry.includes('SQL and raw queries') && sqlTry.includes('redteam-sql'), sqlTry.replace(/\s+/g, ' ').slice(0, 120));
  const knexBefore = await tryIt('const rows = knex.raw(input);');
  await js(`(() => { const i = document.querySelector('[data-signal=sql] [data-sl-addword]'); i.value = 'knex'; i.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(150);
  const knexAfter = await tryIt('const rows = knex.raw(input);');
  check('a word you add makes the signal found', !knexBefore.includes('redteam-sql') && knexAfter.includes('redteam-sql'));
  const tickSql = (on) => js(`(() => { const b = document.querySelector('[data-sp=redteam-sql] [data-sl-trig=sql]'); b.checked = ${on}; b.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await tickSql(false);
  await sleep(150);
  const neverRuns = await js('document.querySelector("[data-sp=redteam-sql]").innerText.includes("never runs")');
  const tableSays = await js('document.querySelector("[data-signal=sql]").textContent.includes("nothing runs on it")');
  check('a preset with nothing ticked is warned on its card and in the table', neverRuns && tableSays);
  await tickSql(true);
  await sleep(150);
  const boxes = await js('document.querySelectorAll("[data-sp=redteam-sql] [data-sl-trig]").length');
  const trigCols = await js('getComputedStyle(document.querySelector("[data-sp=redteam-sql] .trig-block .legend-cols")).columnCount');
  check('each card ticks its signals in two columns', boxes === 15 && trigCols === '2', `${boxes} boxes, ${trigCols} columns`);
  // a check's own word: a piece of code that starts it, even with no signal for it
  await js(`(() => { const i = document.querySelector('[data-sp=redteam-command] [data-sl-addpattern]'); i.value = 'Process.Start('; i.dispatchEvent(new Event('change', {bubbles:true})); })()`);
  await sleep(150);
  await js(`(() => { const b = document.querySelector('[data-sp=redteam-command] [data-sl-tick=codex]'); if (!b.checked) { b.checked = true; b.dispatchEvent(new Event('change', {bubbles:true})); } })()`);
  await sleep(150);
  const ownWord = await tryIt('var p = Process.Start(fileName);');
  check('a check\'s own word starts it from Try it', ownWord.includes('redteam-command'), ownWord.replace(/\s+/g, ' ').slice(0, 140));
  const badRegex = await js(`(() => { const i = document.querySelector('[data-sp=redteam-command] [data-sl-addpattern]'); i.value = '/([a-z/'; i.dispatchEvent(new Event('change', {bubbles:true}));
    return document.querySelector('#toast').textContent; })()`);
  check('an invalid /regex/ is refused, saying so', badRegex.includes('not a valid regular expression'));
  await js('document.querySelector("[data-sp=redteam-command]").scrollIntoView({block:"start"})');
  await shot('p-security-card-triggers', 1920, 1300);
  const kinds = await js('[...document.querySelectorAll(".pcard")].map(c=>c.className.split(" ")[1]).join(",")');
  check('cards say shipped / edited / custom', kinds.includes('edited') && kinds.includes('mine') && kinds.includes('shipped'), kinds);

  // an old coai-mcp: the page says where a control is ignored; the Commands editor lists the shipped orders
  const setMcp = (v) => js(`(() => { const s = document.querySelector('#mcp'); s.value = ${JSON.stringify(v)}; s.dispatchEvent(new Event('change')); })()`);
  await setMcp('0.36.0');
  await js('document.querySelector("[data-tab=models]").click()');
  await sleep(200);
  const cardSkew = await js('(document.querySelector("#card-qwen .skew") || {}).textContent || ""');
  check('an old coai-mcp: an API-key model says it is ignored', cardSkew.includes('a model reached by an API key'), cardSkew.slice(0, 120));
  await js('document.querySelector("[data-tab=security]").click()');
  await sleep(200);
  const laneSkew = await js('[...document.querySelectorAll("#pane .skew")].some((n) => n.textContent.includes("0.41.0"))');
  check('an old coai-mcp: the Security lane says it needs 0.41.0', laneSkew);
  await shot('p-skew-security', 1920, 900);
  await setMcp('0.44.0');
  await js('document.querySelector("[data-tab=reviews]").click()');
  await js('document.querySelector("[data-sub=commands]").click()');
  await sleep(200);
  const shippedOrders = await js('document.querySelector("#pane").innerText.includes("Split with the strongest model") && document.querySelector("#pane").innerText.includes("Work autonomously")');
  check('the Commands editor shows the shipped orders', shippedOrders);
  await shot('p-reviews-commands', 1920, 1300);

  // the last switched-on model for plan review cannot be switched off
  await js('document.querySelector("[data-tab=models]").click()');
  await sleep(150);
  const lastLocked = await js(`(() => {
    const plan = [...document.querySelectorAll('.card')].filter((c) => c.querySelector('[data-feature=plan]')?.checked && c.querySelector('[data-field=enabled]').checked);
    return plan.length !== 1 || plan[0].querySelector('[data-feature=plan]').disabled; })()`);
  check('the last plan model cannot leave plan review', lastLocked);

  // narrow + light
  await js('document.querySelector("[data-tab=models]").click()');
  await js(`(() => { const t = document.querySelector('#theme'); t.value = 'light'; t.dispatchEvent(new Event('change')); })()`);
  await sleep(300);
  const colsNarrow = await (async () => { await send('Emulation.setDeviceMetricsOverride', { width: 900, height: 1500, deviceScaleFactor: 1, mobile: false }); await sleep(250);
    return js('getComputedStyle(document.querySelector(".cards")).gridTemplateColumns.split(" ").length'); })();
  check('one column at 900', colsNarrow === 1, String(colsNarrow));
  await shot('07-light-narrow', 900, 1500);
  await js('document.querySelector("[data-tab=notes]").click()');
  await sleep(200);
  await shot('08-notes', 1920, 1300);
  check('no script errors', errors.length === 0, errors.join(' | '));
} catch (e) {
  checks.push(`FAIL driver — ${e.stack}`);
} finally {
  console.log(checks.join('\n'));
  console.log(`screenshots: ${OUT}`);
  process.exitCode = checks.some((c) => c.startsWith('FAIL')) ? 1 : 0;
  ws?.close();
  chrome.kill();
}
