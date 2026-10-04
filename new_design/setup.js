/* ConnectOtherAIs settings mockup: the Setup tab — Vendor keys, Team servers, MCP server, This side.
   Renders from window.COAI_APP's state and edits only `teamServers` and `setup`. Plain script.
   Faithful to src_vs_code: panelView (keysBody, serverBody, storageBlock), teamServerView,
   dataCommands (Change / Move / Delete old), configTransfer and help.ts. */
(function () {
  'use strict';

  const { VAULT_KEYS, CLIENTS, VENDORS } = window.COAI;
  const app = () => window.COAI_APP;
  const esc = (s) => app().esc(s);
  const st = () => app().state;
  const toast = (text) => app().toast(text);

  const SIMULATED = 'In the extension this opens ';
  const DEFAULT_DIR = '%LOCALAPPDATA%\\coai-mcp';
  const DEMO_ACCOUNT = 'oleksandr.dubyna@fasttask.net';
  /* SERVER_CONTRACT_REQUIRED in teamServerView.ts: the API version this extension needs from a Team server. */
  const CONTRACT_REQUIRED = 1;
  const SIDE_GRAMMAR = 'lower-case letters, digits, dot, dash and underscore';

  /* The "?" texts. help.ts where it has the entry; the setting's own description in package.json where it does not
     (dataDirectory, dataSide, alsoWatchDataDirectories, teamServers); the update sentence from panelProvider's comment. */
  const HELP = {
    credsKey: 'The CredsForDevs config-entry key that unlocks the vendor API keys. It is a pass to one vault entry — revocable, and useless while VS Code is closed — not a secret itself. Vendors whose CLI is signed in need no key at all.',
    perSideSettings: 'One machine can hold several working environments - a local window, and each WSL distro or remote host. VS Code resolves these settings from the settings.json of the CLIENT and hands the same values to every one of them, so without this switch three companies share one proxy, one set of CLI paths and one vault key. On, each side keeps its own values, seeded from what it had when you switched it on - so nothing changes until you edit something, and switching off and on again does not discard what a side had configured. Your text size and help language stay shared: they belong to you rather than to the work.',
    dataDirectory: 'Where the rounds database, the sessions and the chats are kept. Empty is the default folder (%LOCALAPPDATA%\\coai-mcp, or ~/.local/share/coai-mcp). Point it at a network drive or a NAS and your history survives reinstalling the operating system: the same folder is picked up again the next time you install the MCP server. Always kept per side of this machine, whatever Separate settings for each side says — the same NAS is Z:\\coai in a Windows window and /mnt/z/coai in a WSL one, so one shared value would be wrong in one of them. A server only reads it from its own MCP client entry, which is why Install the MCP server… puts it in the block it copies.',
    dataSide: 'A name for this installation inside the folder above, so two that share one folder keep their own database, sessions and sign-ins — a NAS reached from both Windows and WSL. Empty means the folder is not divided, which is right when only this installation uses it. Trimmed and lower-cased before it is used, so Windows and windows are one name; what is left may contain letters, digits, dot, dash and underscore, and anything else is refused rather than quietly ignored — the server will not start on it. Ignored when no folder is chosen: a side only divides a folder you named.',
    alsoWatch: 'Data folders belonging to other installations, whose questions this window should also answer. A round that calls for a human writes its question into the data folder of whichever installation is running it — so a Claude Code session inside WSL writes into the WSL folder, and a Windows window watching only its own never shows the modal while that round blocks. Only the questions are shared — no database is opened across the boundary.',
    teamServers: 'Team servers — one company subscription per vendor, running the CLIs on one machine. Signing in uses your work Microsoft account and the token is kept in a file only you can read, never in settings.',
    cliActions: 'Open this vendor’s own CLI in a terminal, with its usage command ready at the prompt — press Enter to see what you have spent. This is also where you sign a CLI in: a vendor whose CLI is not authenticated fails every round with a timeout. ⤓ Install: install this vendor’s CLI. It opens a terminal with the exact command typed and waiting, picked for the OS the terminal will actually run in — only the vendors’ own published sources: npm for Codex, Google’s own script for Antigravity. ⟳ Update: the vendor’s own update command, typed and waiting — claude update, agy update, and codex update from codex 0.126.0; an older codex is installed again.',
  };
  const help = (key) => app().help(HELP[key]);

  function html(sub) {
    return (PAGES[sub] || PAGES.keys)();
  }

  /* ---------- Vendor keys ---------- */

  function keysPage() {
    const s = st().setup;
    return `<p class="lead">Where the API keys your models use come from. A key's VALUE is never shown here and never
        written to a settings file: coai-mcp reads it from one CredsForDevs entry when it starts.</p>
      <div class="plan-grid">${vaultPanel(s)}${keysInUsePanel(s)}${cliPanel()}${unusedKeysPanel(s)}</div>`;
  }

  function vaultPanel(s) {
    const state = s.vaultAnswered
      ? `<span class="state ok">The vault answered — ${VAULT_KEYS.length} key names</span>`
      : '<span class="state warn">Not checked yet</span>';
    return `<section class="panel"><h3>Where the keys are kept</h3>
      <label class="field"><span>CredsForDevs config key${help('credsKey')}</span>
        <span class="inline"><input type="password" id="creds-key" data-setup-field="credsKey" value="${esc(s.credsKey)}"
          autocomplete="off" spellcheck="false" style="flex: 1">
        <button type="button" class="btn small ghost" data-setup="toggle-creds" aria-controls="creds-key">Show</button></span></label>
      <p class="inline" style="margin: 8px 0">${state}
        <button type="button" class="btn small" data-setup="check-vault">Check the vault</button></p>
      <details class="more"><summary>How to set it up <span class="what">three steps, once</span></summary>
        <ol class="steps"><li>In CredsForDevs, make ONE entry of kind <code>config</code>: a JSON object keyed by key name.</li>
          <li>Turn on <i>Enable Code Access…</i> for it.</li><li>Paste the key it mints above.</li></ol></details>
    </section>`;
  }

  /* Every key name any instance reads — reviewers, consultants, chat alike — and whether the vault has it. */
  function keysInUsePanel(s) {
    const needing = st().instances.filter((i) => VENDORS[i.vendor].connection.includes('key'));
    const byKey = new Map();
    needing.forEach((i) => byKey.set(i.key, [...(byKey.get(i.key) || []), i]));
    const rows = [...byKey].map(([key, list]) => keyRow(key, list, s)).join('');
    const body = rows || '<tr><td colspan="3"><i>No model needs a key — every one signs in through its own CLI.</i></td></tr>';
    return `<section class="panel"><h3>Keys your models use</h3>
      <table class="map"><thead><tr><th scope="col">Key name</th><th scope="col">Used by</th><th scope="col">In the vault</th></tr></thead>
      <tbody>${body}</tbody></table>
      <p class="from">Every model counts here, switched off or not — a key is needed the moment it is switched on.</p></section>`;
  }

  function keyRow(key, list, s) {
    const users = list.map((i) => `${esc(i.name)}${i.enabled ? '' : ' (off)'}`).join(', ');
    if (!key) {
      return `<tr><td><i>no key chosen</i></td><td>${users}</td><td><span class="state err">cannot run</span>
        <a href="#models" data-goto-models>choose one on Models</a></td></tr>`;
    }
    const found = s.vaultAnswered && VAULT_KEYS.includes(key);
    const cell = !s.vaultAnswered ? '<span class="state warn">not checked</span>'
      : found ? '<span class="state ok">found</span>' : '<span class="state err">missing — add it to the entry</span>';
    return `<tr><td><code>${esc(key)}</code></td><td>${users}</td><td>${cell}</td></tr>`;
  }

  /* One row per CLI a model runs on. DeepSeek and OpenRouter ride the Codex CLI against their own endpoints. */
  const CLI_GROUPS = [
    { id: 'codex', label: 'Codex CLI', bin: 'codex', vendors: ['codex'], probe: 'codex' },
    { id: 'codex-keyed', label: 'Codex CLI', bin: 'codex', vendors: ['deepseek', 'openrouter'], probe: 'codex',
      note: 'for DeepSeek / OpenRouter — the same binary, pointed at their endpoints' },
    { id: 'claude', label: 'Claude Code', bin: 'claude', vendors: ['claude'], probe: 'claude' },
    { id: 'antigravity', label: 'Antigravity', bin: 'agy', vendors: ['antigravity'], probe: 'antigravity' },
  ];
  /* What the version probe read on this side (demo): `installed` '' is missing, 'unknown' answered without a version. */
  const CLI_PROBE = {
    codex: { installed: '0.156.1', latest: '0.157.0' },
    claude: { installed: '2.1.258', latest: '2.1.258' },
    antigravity: { installed: 'unknown', latest: '' },
  };
  const USAGE = { codex: '/status', claude: '/usage', antigravity: 'agy usage' };
  const INSTALL = {
    codex: 'npm install -g @openai/codex',
    claude: 'npm install -g @anthropic-ai/claude-code',
    antigravity: 'Google’s own install script from antigravity.google',
  };
  let agyMissing = false; // mockup-only switch, so the Install state can be seen

  const instancesOf = (g) => st().instances.filter((i) => g.vendors.includes(i.vendor));
  const probeOf = (g) => (agyMissing && g.probe === 'antigravity' ? { installed: '', latest: '' } : CLI_PROBE[g.probe]);
  const hasNewer = (p) => /^\d/.test(p.installed) && p.latest !== '' && app().older(p.installed, p.latest);

  function cliPanel() {
    const rows = CLI_GROUPS.filter((g) => instancesOf(g).length).map(cliRow).join('');
    return `<section class="panel"><h3>CLIs your models run on</h3>
      <p class="from">These need no key here: each CLI keeps its own sign-in.
        Signing in happens in the CLI itself — open it and follow its prompt; coai cannot see whether you are signed in.</p>
      <table class="map"><thead><tr><th scope="col">CLI</th><th scope="col">Models</th><th scope="col">Version</th>
        <th scope="col">Actions${help('cliActions')}</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="4"><i>No model runs on a CLI.</i></td></tr>'}</tbody></table>
      <label class="inline from" style="margin-top: 8px"><input type="checkbox" data-setup-field="demoAgyMissing"${agyMissing ? ' checked' : ''}>
        <span class="placeholder-tag">mockup</span> pretend Antigravity is not installed on this side</label></section>`;
  }

  function cliRow(g) {
    const p = probeOf(g);
    const models = instancesOf(g).map((i) => `${esc(i.name)}${i.enabled ? '' : ' <i>(off)</i>'}`).join(', ');
    const note = g.note ? `<br><span class="from">${esc(g.note)}</span>` : '';
    return `<tr data-cli="${esc(g.id)}"><td>${esc(g.label)} <span class="mono small">${esc(g.bin)}</span>${note}</td>
      <td>${models}</td><td>${versionCell(p)}</td><td><div class="cli-actions">${cliButtons(p)}</div></td></tr>`;
  }

  function versionCell(p) {
    if (!p.installed) return '<span class="state err">not found</span>';
    if (p.installed === 'unknown') return '<span class="state ok">installed</span><br><span class="from">version unknown</span>';
    if (hasNewer(p)) return `<b>${esc(p.installed)}</b><br><span class="state warn">${esc(p.latest)} available</span>`;
    return `<b>${esc(p.installed)}</b><br><span class="state ok">current</span>`;
  }

  function cliButtons(p) {
    if (!p.installed) return '<button type="button" class="btn small primary" data-setup="cli-install">⤓ Install</button>';
    const newer = hasNewer(p);
    return `<button type="button" class="btn small" data-setup="cli-open">▶ Open in a terminal</button>
      <button type="button" class="btn small${newer ? ' update-ready' : ' ghost'}" data-setup="cli-update">⟳ Update${newer ? ` to ${esc(p.latest)}` : ''}</button>`;
  }

  const groupOf = (t) => CLI_GROUPS.find((g) => g.id === t.closest('[data-cli]').dataset.cli);

  /* runVendor: the command is sent, the usage command typed and left waiting. */
  function cliOpen(g) {
    const first = instancesOf(g)[0];
    const flag = g.bin === 'codex' ? '-m' : '--model';
    const keyed = g.id === 'codex-keyed'
      ? ` It reaches ${VENDORS[first.vendor].endpoint}; reviews take its key from the vault, this terminal does not — export ${first.vendor.toUpperCase()}_API_KEY first if the CLI asks.`
      : '';
    toast(`${SIMULATED}a terminal "coai · ${first.vendor}" and runs ${g.bin} ${flag} ${first.model}, with ${USAGE[g.probe]} typed and waiting. Sign in there if it asks.${keyed}`);
  }

  function cliInstall(g) {
    toast(`${SIMULATED}a terminal "coai · install ${g.probe}" with ${INSTALL[g.probe]} typed and waiting — and, if this machine has no node, winget install OpenJS.NodeJS.LTS first.`);
  }

  function cliUpdate(g) {
    const p = probeOf(g);
    const codexSelf = /^\d/.test(p.installed) && !app().older(p.installed, '0.126.0');
    const command = g.probe !== 'codex' ? `${g.bin} update` : codexSelf ? 'codex update' : 'npm install -g @openai/codex@latest';
    toast(`${SIMULATED}a terminal "coai · update ${g.probe}" with ${command} typed and waiting. Press Enter when you are ready.`);
  }

  function unusedKeysPanel(s) {
    const used = new Set(st().instances.map((i) => i.key));
    const unused = VAULT_KEYS.filter((k) => !used.has(k));
    if (!s.vaultAnswered) return '';
    const list = unused.length ? unused.map((k) => `<li><code>${esc(k)}</code></li>`).join('') : '<li><i>none — every key is in use</i></li>';
    return `<section class="panel"><h3>In the vault, used by no model</h3><ul>${list}</ul>
      <p class="from">Harmless. Pick one on a model's card under Connection, or remove it in CredsForDevs.</p></section>`;
  }

  /* ---------- Team servers ---------- */

  let adding = null;
  let busy = {};      // server id → what it is in the middle of ("Signing in…"); its buttons are disabled meanwhile
  let failing = [];   // server ids whose last check failed — their catalog is shown as stale

  /* Per-vendor account states the server reports (demo). Anything not listed has every slot ready. */
  const ACCOUNTS = {
    'remsoft/codex': { ready: 2, cooling: 1, coolMin: 4, signedOut: 0 },
    'remsoft/claude': { ready: 1, cooling: 0, coolMin: 0, signedOut: 1 },
  };

  const serverById = (id) => st().teamServers.find((s) => s.id === id);
  const serverOfTarget = (t) => t.closest('[data-server]').dataset.server;
  const without = (obj, key) => Object.fromEntries(Object.entries(obj).filter(([k]) => k !== key));

  function teamPage() {
    const servers = st().teamServers;
    const list = servers.length ? servers.map(serverPanel).join('')
      : `<section class="panel"><h3>No Team server yet</h3><p>A Team server runs the vendor CLIs on one machine, on one
          company subscription, so nobody needs their own. Add one and sign in with your work account.</p></section>`;
    return `<p class="lead">Company servers that review for you. Their vendors become models you add on the Models tab.</p>
      <div class="toolbar"><button type="button" class="btn primary" data-setup="add-server">＋ Add a Team server</button>${help('teamServers')}</div>
      ${adding ? addServerPanel() : ''}
      <div class="plan-grid">${list}</div>`;
  }

  function serverPanel(s) {
    return `<section class="panel server" style="--vc: var(--vc-slate)" data-server="${esc(s.id)}">
      <h3>${esc(s.name)} <span class="from">${esc(s.url)}</span></h3>
      <p class="inline">${s.signedIn ? `<span class="state ok">signed in</span> as ${esc(s.account)}` : '<span class="state warn">no account</span>'}
        ${s.signedIn && s.version ? `<span class="from">· server ${esc(s.version)} · API version ${esc(s.contract ?? '?')}</span>` : ''}</p>
      <p class="${busy[s.id] ? 'state warn' : 'from'}">${esc(statusSentence(s))}</p>
      ${contractNote(s)}${publishedNote(s)}
      <p class="callout warn">${esc(disclosure(s.url))}</p>
      ${s.signedIn ? vendorsTable(s) : ''}
      ${serverModels(s)}
      ${serverButtons(s)}
    </section>`;
  }

  function problemOf(s) {
    if (failing.includes(s.id)) return 'The server did not answer within 10 seconds';
    return s.unreached ? 'it did not answer when it was added' : '';
  }

  /* teamServerView.statusSentence + notSignedInHere. */
  function statusSentence(s) {
    if (busy[s.id]) return busy[s.id];
    const problem = problemOf(s);
    if (!s.signedIn) return notSignedInHere(s.elsewhere || '', problem);
    if (problem) return `${problem} — showing what it last said.`;
    return `Signed in. Server ${s.version}.`;
  }

  function notSignedInHere(elsewhere, problem) {
    if (!problem) {
      return elsewhere ? `${elsewhere} is signed in on another side of this machine — press Sign in to use it here.` : 'Not signed in.';
    }
    return elsewhere
      ? `${elsewhere} is signed in on another side of this machine, and this side could not sign itself in: ${problem}`
      : `Not signed in — ${problem}`;
  }

  /* Silent when the server never answered (undefined) or is new enough. 0 = it answered and named nothing. */
  function contractNote(s) {
    if (s.contract === undefined || s.contract === null || s.contract >= CONTRACT_REQUIRED) return '';
    const speaks = s.contract === 0 ? 'does not say which version of the API it speaks' : `speaks version ${s.contract} of the API`;
    return `<p class="callout warn">⚠ This server ${esc(speaks)}, and this extension needs ${CONTRACT_REQUIRED} or later.
      Reviews sent here may be read wrongly by one half or the other — update the Team server.</p>`;
  }

  /* Admins only, read-only: a Team server is deployed, not downloaded. */
  function publishedNote(s) {
    if (!s.admin || !s.signedIn || !s.version || !s.published) return '';
    const text = app().older(s.version, s.published)
      ? `⬆ ${s.published} is published — this one runs ${s.version}.`
      : `${s.published} is the newest published — this one is up to date.`;
    return `<p class="from">You are an administrator here. ${esc(text)}</p>`;
  }

  function disclosure(url) {
    let host = url;
    try { host = new URL(url).host; } catch { /* shown as typed */ }
    return `Every review sent here — your plan, your diffs and the file contents around them — goes to ${host}.`;
  }

  function vendorsTable(s) {
    const stale = problemOf(s) !== '';
    const rows = s.vendors.map((v) => {
      const a = ACCOUNTS[`${s.id}/${v.id}`] || { ready: v.slots, cooling: 0, coolMin: 0, signedOut: 0 };
      return `<tr><td>${esc(v.id)}</td><td>${v.models.map(esc).join(', ')}</td><td>${esc(accountsSentence(a))}</td><td>${healthCell(a)}</td></tr>`;
    }).join('');
    return `<div class="block"><h4 class="block-title">${stale ? 'What it offered when it last answered' : 'What it offers'}</h4>
      ${stale ? '<p class="callout warn">A stale catalog: this is the last answer, and it may be out of date until the server answers again.</p>' : ''}
      <table class="map"><thead><tr><th scope="col">Vendor</th><th scope="col">Models</th><th scope="col">Accounts</th><th scope="col">Health</th></tr></thead>
      <tbody>${rows}</tbody></table>
      <p class="from">Roles it runs: ${s.roles.map(esc).join(', ')}.</p></div>`;
  }

  /* slotSentence's states, said in full: rate-limited accounts come back by themselves, signed-out ones never do. */
  function accountsSentence(a) {
    const total = a.ready + a.cooling + a.signedOut;
    if (total === 0) return 'no accounts — ask the operator';
    if (a.ready === 0 && a.cooling === 0) return `all ${total} signed out — the operator must sign them in`;
    const cooling = a.cooling ? ` (rate-limited, ${a.coolMin} min)` : '';
    return `${a.ready} ready · ${a.cooling} cooling down${cooling} · ${a.signedOut} signed out`;
  }

  function healthCell(a) {
    if (a.ready > 0) return '<span class="state ok">answering</span>';
    return a.cooling > 0 ? '<span class="state warn">waiting</span>' : '<span class="state err">no account</span>';
  }

  function serverModels(s) {
    const models = st().instances.filter((i) => i.vendor === 'remote' && i.server === s.id);
    const list = models.length
      ? `<ul>${models.map((i) => `<li>${esc(i.name)} — ${esc(i.serverVendor)} · ${esc(i.model)}${i.enabled ? '' : ' <i>(off)</i>'}</li>`).join('')}</ul>`
      : '<p class="from">None yet.</p>';
    return `<div class="block"><h4 class="block-title">Models from this server</h4>${list}
      <button type="button" class="btn small" data-setup="add-model" ${s.signedIn ? '' : 'disabled title="Sign in first"'}>＋ Add a model from this server</button></div>`;
  }

  function serverButtons(s) {
    const stop = busy[s.id] ? ' disabled' : '';
    return `<div class="inline" style="margin-top: 10px">
      <button type="button" class="btn small" data-setup="${s.signedIn ? 'sign-out' : 'sign-in'}"${stop}>${s.signedIn ? 'Sign out' : 'Sign in'}</button>
      <button type="button" class="btn small ghost" data-setup="recheck-server"${stop}>Check again</button>
      <button type="button" class="btn small ghost danger" data-setup="remove-server"${stop}>Remove</button></div>`;
  }

  function addServerPanel() {
    const check = adding.checking ? '<span class="state warn">checking…</span>'
      : adding.problem ? `<span class="state err">${esc(adding.problem)}</span>` : '';
    return `<section class="panel" style="margin-bottom: 14px"><h3>Add a Team server</h3>
      <div class="settings-grid">
        <label class="field"><span>Name</span><input type="text" data-add-server="name" value="${esc(adding.name)}" placeholder="RemSoft Dev"></label>
        <label class="field"><span>Address</span><input type="text" data-add-server="url" value="${esc(adding.url)}" placeholder="https://coai.example.com"></label>
      </div>
      ${adding.unreachable ? unreachableHtml() : ''}
      <p class="inline" style="margin-top: 10px">
        <button type="button" class="btn primary small" data-setup="add-server-commit"${adding.checking ? ' disabled' : ''}>Check and add</button>
        <button type="button" class="btn small" data-setup="add-server-cancel">Cancel</button> ${check}</p>
    </section>`;
  }

  /* panelProvider.reachable(): refused with the reason, and the person decides. */
  function unreachableHtml() {
    const host = new URL(adding.url.trim()).host;
    return `<div class="callout warn"><p><b>${esc(adding.name.trim())} could not be checked: ${esc(host)} did not answer (connection refused).</b></p>
      <p class="from">A server that is only down right now is not a mistake — but a wrong address is, and it would otherwise surface
        as a broken reviewer days from now.</p>
      <button type="button" class="btn small" data-setup="add-server-anyway">Add it anyway</button></div>`;
  }

  function addRefusal(a) {
    if (!a.name.trim()) return 'A name is needed';
    if (!a.url.trim().startsWith('http')) return 'An https address is needed';
    try { new URL(a.url.trim()); } catch { return 'That is not an address.'; }
    return '';
  }

  function setAdding(change) {
    adding = { ...adding, ...change };
    app().render();
  }

  function addServer() {
    const problem = addRefusal(adding);
    if (problem) return setAdding({ problem, unreachable: false });
    setAdding({ checking: true, problem: '', unreachable: false });
    setTimeout(answered, 700);
  }

  /* A host containing "offline", "down" or "unreachable" plays a server that does not answer. */
  function answered() {
    if (!adding) return;
    const url = new URL(adding.url.trim());
    if (/offline|down|unreachable/i.test(url.host)) return setAdding({ checking: false, unreachable: true });
    commitServer(url, true);
  }

  function newServerId(name) {
    const taken = st().teamServers.map((s) => s.id);
    const base = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'server';
    let id = base;
    for (let n = 2; taken.includes(id); n += 1) id = `${base}-${n}`;
    return id;
  }

  /* A server added by this flow plays the other two states: signed in on another side, and an old API contract. */
  function commitServer(url, reached) {
    const name = adding.name.trim();
    const server = { id: newServerId(name), name, url: url.origin, account: '', signedIn: false,
      elsewhere: reached ? DEMO_ACCOUNT : '', unreached: !reached, version: reached ? '0.9.0' : '', published: '0.9.0',
      contract: reached ? 0 : undefined, admin: false, roles: ['PlanCritique', 'Conventions', 'Architecture', 'SecurityReliability', 'UxDxPerformance'],
      vendors: [{ id: 'codex', runtime: 'codex', models: ['gpt-6-luna'], health: 'ok', slots: 2, busy: 0 }] };
    adding = null;
    app().setState({ ...st(), teamServers: [...st().teamServers, server] });
    app().render();
    toast(reached ? `${name} answered (server 0.9.0). Sign in to use it.` : `${name} was added without being checked.`);
  }

  function signIn(id) {
    busy = { ...busy, [id]: 'Signing in…' };
    app().render();
    setTimeout(() => {
      busy = without(busy, id);
      const s = serverById(id);
      if (s) patchServer(id, { signedIn: true, account: s.elsewhere || DEMO_ACCOUNT, elsewhere: '', unreached: false });
      else app().render();
    }, 1400);
  }

  /* The mockup's "Check again" alternates: a failed check keeps the last catalog and says so. */
  function recheck(id) {
    failing = failing.includes(id) ? failing.filter((x) => x !== id) : [...failing, id];
    app().render();
    toast(failing.includes(id) ? 'The server did not answer — the panel keeps what it last said.' : 'The server answered just now.');
  }

  function confirmRemoveServer(id) {
    const s = serverById(id);
    const models = st().instances.filter((i) => i.vendor === 'remote' && i.server === id);
    const also = models.length ? `<label class="inline"><input type="checkbox" id="remove-server-models" checked>
        Also remove its ${models.length} model(s): ${models.map((i) => esc(i.name)).join(', ')}</label>
      <p class="from">Kept, they stay on Models as "a removed server" and cannot run until you point them at another.</p>` : '';
    app().confirm({
      title: `Remove ${s.name}?`, action: 'Remove', danger: true,
      body: `<p>You will be signed out of it on this side.</p>${also}`,
      onConfirm: () => removeServer(id, Boolean(document.getElementById('remove-server-models')?.checked)),
    });
  }

  function removeServer(id, alsoModels) {
    if (alsoModels) app().removeInstances(st().instances.filter((i) => i.vendor === 'remote' && i.server === id).map((i) => i.id));
    app().setState({ ...st(), teamServers: st().teamServers.filter((s) => s.id !== id) });
    app().render();
  }

  function patchServer(id, change) {
    app().setState({ ...st(), teamServers: st().teamServers.map((s) => (s.id === id ? { ...s, ...change } : s)) });
    app().render();
  }

  /* ---------- MCP server ---------- */

  function mcpPage() {
    const s = st().setup;
    return `<p class="lead">coai-mcp is the server your AI talks to: it runs the rounds, the consultant and the question
        consultant. This is what is installed on this side, which AI clients use it, and where it keeps its data.</p>
      <div class="plan-grid">${installedPanel(s)}${clientsPanel()}${dataPanel(s)}</div>`;
  }

  function installedPanel(s) {
    const v = app().mcpVersion();
    return `<section class="panel"><h3>coai-mcp on this side</h3>
      ${v ? installedBody(s, v) : absentBody(s)}
      <p class="from">Settings changed here reach it on its next call — it re-reads the file whenever it changes.
        The config block is pasted into your AI client once, when you first set it up.</p>
      <div class="inline"><button type="button" class="btn small ghost" data-setup="check-mcp">Check again</button>
        <span class="from">Checked ${esc(s.mcp.checked)}.</span></div></section>`;
  }

  function absentBody(s) {
    const planned = app().PLANNED;
    return `<p><span class="state warn">not installed</span> coai-mcp is not installed on this side (${esc(s.side)}).</p>
      <p class="from">${esc(planned)} is published.</p>
      <p><button type="button" class="btn primary small" data-setup="install-mcp">⬇ Install coai-mcp ${esc(planned)}</button></p>`;
  }

  function installedBody(s, v) {
    const planned = app().PLANNED;
    const runnable = st().instances.filter((i) => i.enabled).length;
    const update = app().older(v, planned)
      ? `<p class="from">${esc(planned)} is published.</p>
         <p><button type="button" class="btn primary small" data-setup="update-mcp">⬇ Update to ${esc(planned)}</button></p>`
      : `<p class="from">${esc(planned)} is the newest published — you are up to date.</p>`;
    // 0.36.0 plays a binary whose providers probe was asked and could not answer.
    const probe = v === '0.36.0'
      ? `<p class="callout warn">The installed coai-mcp could not report its reviewers, so no card can say whether the server
           would run it. An update usually fixes it.</p>`
      : `<p class="from">It reports ${runnable} model(s) switched on, and can run every one of them.</p>`;
    return `<p><span class="state ok">installed</span> coai-mcp <b>${esc(v)}</b> is installed in ${esc(s.side)}.</p>
      <p class="mono">${esc(s.mcp.path)}</p>${update}${probe}`;
  }

  /* The top bar's "coai-mcp on this side" switch is the mockup's stand-in for the binary; an install moves it. */
  function installMcp(verb) {
    const planned = app().PLANNED;
    const after = verb === 'install' ? ', then asks where your data should live and puts the config block on your clipboard' : '';
    toast(`In the extension this downloads coai-mcp ${planned} from its GitHub release and puts it in place${after}.`);
    setTimeout(() => {
      const pick = document.getElementById('mcp');
      if (!pick) return;
      pick.value = planned;
      pick.dispatchEvent(new Event('change', { bubbles: true }));
    }, 900);
  }

  const AI_CLIENTS = CLIENTS.filter((c) => c.id !== 'gemini'); // Gemini CLI is retired

  function clientsPanel() {
    const detected = app().newTag('Today the panel does not look into a client’s config, so it cannot say whether coai is registered there.');
    const rows = AI_CLIENTS.map(clientRow).join('');
    return `<section class="panel"><h3>AI clients that use it</h3>
      <table class="map"><thead><tr><th scope="col">Client</th><th scope="col">Entry${detected}</th><th scope="col">Config block</th></tr></thead>
      <tbody>${rows}</tbody></table>
      <div class="block"><h4 class="block-title">Gate instructions</h4>
        <p>This repository's <code>CLAUDE.md</code>: <span class="state ok">current</span> ·
          <code>AGENTS.md</code>: <span class="state warn">older than this version</span></p>
        <p class="inline"><button type="button" class="btn small" data-setup="copy-snippet">Copy the gate instructions</button>
          <span class="from">— paste into the file that is behind. Checked per workspace file, not per client.</span></p></div></section>`;
  }

  function clientRow(c) {
    const entry = c.registered ? '<span class="state ok">registered</span>' : '<span class="state warn">not registered</span>';
    const toml = c.id === 'codex';
    const tag = toml ? app().newTag('Today the copied block is the JSON mcpServers block; Codex reads TOML.') : '';
    return `<tr><td>${esc(c.label)}<br><span class="mono small">${esc(c.file)}</span></td><td>${entry}</td>
      <td><button type="button" class="btn small" data-setup="copy-config" data-client="${esc(c.id)}">Copy ${toml ? 'TOML' : 'JSON'} block</button>${tag}</td></tr>`;
  }

  function configBlock(clientId) {
    const command = st().setup.mcp.path;
    const env = envOf(st().setup.data);
    if (clientId !== 'codex') return JSON.stringify({ mcpServers: { coai: { command, env } } }, null, 2);
    const envLines = Object.entries(env).map(([k, v]) => `${k} = ${JSON.stringify(v)}`).join('\n');
    return `[mcp_servers.coai]\ncommand = ${JSON.stringify(command)}\n${envLines ? `\n[mcp_servers.coai.env]\n${envLines}\n` : ''}`;
  }

  /* ---------- where this window keeps its data ---------- */

  /* A second watched folder, with the refusal the panel names rather than swallowing (demo). */
  const WATCHED_DEMO = [{ asked: '\\\\nas\\coai\\laptop', refusal: 'not readable from this side' }];
  let demoOpen = false; // the mockup-only storage-states box stays open while it is being used
  const CAME_FROM = {
    environment: 'From <code>COAI_DATA_DIR</code> in this window\'s own environment, which outranks any setting.',
    'this side': 'Chosen for this side of the machine.',
    'shared setting': 'From <code>coai.dataDirectory</code>, set for every side of this machine — check it is a path that exists on THIS side, since the same drive is reached by a different route from Windows and from WSL.',
    default: 'The default folder — nothing has been chosen, so a reinstalled machine starts a fresh history here.',
  };

  const currentDir = (d) => (d.source === 'default' ? DEFAULT_DIR : d.directory);
  const sideOf = (d) => (d.source === 'default' ? '' : (d.sideName || ''));
  const joinDir = (dir, side) => (side ? `${dir.replace(/[\\/]+$/, '')}\\${side}` : dir);
  const resolvedDir = (d) => joinDir(currentDir(d), sideOf(d));
  const today = () => new Date().toISOString().slice(0, 10);

  function envOf(d) {
    if (d.source === 'default') return {};
    return { COAI_DATA_DIR: d.directory, ...(d.sideName ? { COAI_DATA_SIDE: d.sideName } : {}) };
  }

  function dataPanel(s) {
    const d = s.data;
    return `<section class="panel span-all"><h3>Where this window keeps its data${help('dataDirectory')}</h3>
      <p class="mono">${esc(resolvedDir(d))}</p>
      <p class="from">${CAME_FROM[d.source] || CAME_FROM['this side']}</p>
      ${sideNote(d)}${watchedHtml(d)}
      <p class="from">That is what <b>this window</b> reads. The server your assistant talks to reads whatever its own MCP client
        entry gives it — if the rounds list is empty while your assistant says it is reviewing, the two have come apart.</p>
      ${movedRecord(d)}
      <div class="inline"><button type="button" class="btn small" data-setup="change-dir"${flow ? ' disabled' : ''}>Change where your data lives…</button>
        <button type="button" class="btn small ghost" data-setup="move-dir"${flow ? ' disabled' : ''}>Move what is here to another folder…</button></div>
      ${flow ? flowHtml(d) : ''}
      ${pointHere(d)}
      <details class="more"><summary>Moving by hand <span class="what">only if the button above cannot</span></summary>
        <p class="from">Stop the server, COPY <code>coai.db</code>, <code>sessions/</code>, <code>prompts/</code> and <code>logs/</code> into an
          EMPTY folder, start it again and check the rounds list before deleting anything. Leave <code>worktrees/</code> and the
          sign-ins behind.</p></details>
      ${demoStates(d)}
    </section>`;
  }

  function sideNote(d) {
    if (sideOf(d)) {
      return `<p class="from">Side <b>${esc(sideOf(d))}</b>, so this window keeps its own database, sessions and sign-ins apart
        from any other side sharing that directory.</p>`;
    }
    if (d.ignoredSide) {
      return `<p class="callout warn">You have set <code>COAI_DATA_SIDE=${esc(d.ignoredSide)}</code>, but no <code>COAI_DATA_DIR</code> —
        so nothing is partitioned and this is the default directory. A side only divides a directory you chose.</p>`;
    }
    return `<p class="from">No side is named, so this is the directory itself. Set <code>COAI_DATA_SIDE</code> only when two
      installations share one directory — a NAS reached from both Windows and WSL — and each needs its own database.</p>`;
  }

  function watchedHtml(d) {
    const dirs = [...(d.alsoWatched || []).map((asked) => ({ asked, refusal: '' })), ...WATCHED_DEMO];
    const rows = dirs.map((x) => (x.refusal
      ? `<p class="mono">${esc(x.asked)}</p><p class="from"><span class="state err">${esc(x.refusal)}</span> — its questions cannot reach this window.</p>`
      : `<p class="mono">${esc(x.asked)}</p>`)).join('');
    return `<p class="from">Questions from another installation are also answered here:${help('alsoWatch')}</p>${rows}`;
  }

  function movedRecord(d) {
    if (!d.movedFrom) return '';
    return `<div class="callout"><p>Moved from <code>${esc(d.movedFrom)}</code> on <time datetime="${esc(d.movedOn)}">${esc(d.movedOn)}</time>
        — its copy is still there.</p>
      <button type="button" class="btn small ghost danger" data-setup="delete-old"${flow ? ' disabled' : ''}>Delete the old folder…</button></div>`;
  }

  function pointHere(d) {
    const env = envOf(d);
    if (!Object.keys(env).length) {
      return `<p class="from">This is the default directory, so a server needs no configuration to find it — any client entry
        without a <code>COAI_DATA_DIR</code> reads the same place.</p>`;
    }
    return `<details class="more"><summary>Point a server here <span class="what">the env of its client entry</span></summary>
      <p class="from">Add these to the <code>env</code> of its entry — the rest of the block, with the path to the binary, is what
        <b>Install the MCP server…</b> puts on your clipboard.</p>
      <pre class="paste">${esc(JSON.stringify({ env }, null, 2))}</pre></details>`;
  }

  function demoStates(d) {
    const options = Object.keys(CAME_FROM).map((k) => `<option value="${esc(k)}"${k === d.source ? ' selected' : ''}>${esc(k)}</option>`).join('');
    return `<details class="more"${demoOpen ? ' open' : ''}><summary><span class="placeholder-tag">mockup</span> <span class="what">try the other storage states</span></summary>
      <div class="settings-grid"><label class="field"><span>Where the folder came from</span><select data-setup-field="demoSource">${options}</select></label>
        <label class="inline"><input type="checkbox" data-setup-field="demoIgnoredSide"${d.ignoredSide ? ' checked' : ''}>
          <span><code>COAI_DATA_SIDE</code> set in this window's environment, <code>COAI_DATA_DIR</code> not</span></label></div></details>`;
  }

  /* ---------- the data-folder flows (dataCommands.ts) ---------- */

  /* Not persisted: a flow is something happening now. What IS persisted is the move's record — written only once
     the copy verified and the settings landed — so "Delete the old folder" survives a reload. */
  let flow = null;
  const MOVE_STEPS = [
    'Check where it would land, and that the folder holds no history — one that does is refused',
    'Copy coai.db, sessions/, prompts/, the chats and the rest — 1,284 files, 212 MB',
    'Read the copy back and compare it with what the source held',
    'Switch this window to the new folder',
    'Record the move — nothing is deleted; the old folder stays until you delete it',
  ];

  function setFlow(change) {
    flow = { ...flow, ...change };
    app().render();
  }

  function flowHtml(d) {
    const head = flow.kind === 'change' ? 'Change where your data lives' : 'Move what is here to another folder';
    const body = flow.kind === 'move' ? moveBody(d) : { pick: changePick, confirm: changeConfirm, done: handover }[flow.stage](d);
    return `<div class="flow callout"><h4>${head}</h4>${body}</div>`;
  }

  function folderField() {
    const off = flow.running ? ' disabled' : '';
    return `<div class="inline"><input type="text" data-flow="target" value="${esc(flow.target)}" placeholder="D:\\somewhere\\coai-data"
        aria-label="Folder" style="flex: 1"${off}>
      <button type="button" class="btn small" data-setup="browse"${off}>Browse…</button></div>
      ${flow.problem ? `<p class="from"><span class="state err">${esc(flow.problem)}</span></p>` : ''}`;
  }

  /* ---------- Change ---------- */

  function pickChoice(value, label, detail) {
    return `<label class="choice"><input type="radio" name="change-pick" value="${value}" data-flow="pick"${flow.pick === value ? ' checked' : ''}>
      <span>${esc(label)}</span><span class="d">${esc(detail)}</span></label>`;
  }

  function changePick(d) {
    const choose = flow.pick === 'choose';
    const env = d.source === 'environment'
      ? '<p class="callout warn"><code>COAI_DATA_DIR</code> in this window\'s environment outranks any setting — a choice made here takes effect once it is removed.</p>' : '';
    return `<div class="choices">
        ${pickChoice('default', 'Put it back in the default folder', `${DEFAULT_DIR} — on the system drive, which is the thing that gets reformatted.`)}
        ${pickChoice('choose', 'Choose a folder…', 'One that survives reinstalling this machine — a network drive or a NAS.')}</div>
      ${choose ? folderField() + sideField() : ''}${env}
      <p class="from">This is kept for this side only — every side chooses its own. The history in <code>${esc(resolvedDir(d))}</code>
        stays where it is — use Move to take it along.</p>
      <div class="inline"><button type="button" class="btn primary small" data-setup="change-apply">${choose ? 'Next — see what it holds' : 'Put it back'}</button>
        <button type="button" class="btn small" data-setup="flow-cancel">Cancel</button></div>`;
  }

  function sideField() {
    return `<label class="field" style="margin-top: 6px"><span>Side name in this folder${help('dataSide')}</span>
        <input type="text" data-flow="side" value="${esc(flow.side)}" spellcheck="false" placeholder="empty: the folder is not divided"></label>
      <p class="from">Two installations sharing one folder each keep their own database under their own name. Leave it empty if
        only this one uses the folder. It may contain ${esc(SIDE_GRAMMAR)}.</p>`;
  }

  const cleanSide = (side) => side.trim().toLowerCase();

  function changeRefusal(f) {
    if (!f.target.trim()) return 'Choose a folder first.';
    const side = cleanSide(f.side);
    if (side && !/^[a-z0-9._-]+$/.test(side)) {
      return `A side may contain ${SIDE_GRAMMAR}, and "${f.side.trim()}" does not — the server refuses to start on a name it cannot use rather than quietly sharing one folder.`;
    }
    return '';
  }

  /* What a chosen folder already holds (demo): "history" holds a database of its own, "shared" two other sides. */
  function whatIsIn(target) {
    if (/history/i.test(target)) return { hasDatabase: true, sides: [] };
    if (/shared/i.test(target)) return { hasDatabase: false, sides: ['wsl-ubuntu-24.04', 'laptop'] };
    return { hasDatabase: false, sides: [] };
  }

  /* The histories one could continue: '' is the folder's own database, the rest are other sides' names. */
  const historiesIn = (found) => [...(found.hasDatabase ? [''] : []), ...found.sides];

  function changeApply() {
    if (flow.pick === 'default') return resetToDefault();
    const problem = changeRefusal(flow);
    if (problem) return setFlow({ problem });
    const found = whatIsIn(flow.target.trim());
    setFlow({ stage: 'confirm', problem: '', found, adopt: historiesIn(found)[0] ?? '' });
  }

  /* Shown BEFORE anything is saved. A folder holding a history is ADOPTED, never refused — the reinstall case. */
  function changeConfirm() {
    const target = flow.target.trim();
    const side = cleanSide(flow.side);
    const landing = joinDir(target, side);
    const histories = historiesIn(flow.found);
    const continuing = histories.includes(side);
    const sentence = continuing
      ? `${landing} already holds a database, and this installation will go on writing to it — which is what continues a history across a reinstalled machine. Nothing in it is moved or overwritten by choosing it.`
      : histories.length ? adoptOrBeside(target, side, flow.found)
        : `${landing} has no history in it yet, so this installation starts with none. If that is a surprise, check the path before recording into it — a history you are looking for is in some other folder.`;
    const choose = !continuing && histories.length > 0;
    return `<p><b>Keep this installation's data in <code>${esc(landing)}</code>?</b></p><p class="from">${esc(sentence)}</p>
      ${choose ? adoptPicker(histories) : ''}
      <div class="inline">${continuing || choose ? '<button type="button" class="btn primary small" data-setup="change-continue">Continue this history</button>' : ''}
        ${continuing ? '' : `<button type="button" class="btn${choose ? '' : ' primary'} small" data-setup="change-fresh">Use this folder</button>`}
        <button type="button" class="btn small ghost" data-setup="change-back">Back</button>
        <button type="button" class="btn small" data-setup="flow-cancel">Cancel</button></div>`;
  }

  function adoptOrBeside(target, side, found) {
    if (found.sides.length) {
      const who = found.sides.length === 1 ? 'one installation' : `${found.sides.length} installations`;
      return `${target} is already shared by ${who}: ${found.sides.join(', ')}. Name this one the same as the side whose history you are continuing, or give it a name of its own to start beside them.`;
    }
    return `${target} already holds a database of its own. Continue that history — nothing in it is moved or overwritten — or use this folder under the side ${side}, which starts a database of its own beside it.`;
  }

  function adoptPicker(histories) {
    const options = histories.map((h) => `<option value="${esc(h)}"${h === flow.adopt ? ' selected' : ''}>${h ? esc(h) : 'the folder itself — no side'}</option>`).join('');
    return `<label class="field"><span>The history to continue</span><select data-flow="adopt">${options}</select></label>`;
  }

  /* Continue: the typed side when it already holds a history, otherwise the one picked from the list. */
  function continueHistory() {
    const side = cleanSide(flow.side);
    applyChange(flow.target.trim(), historiesIn(flow.found).includes(side) ? side : flow.adopt);
  }

  function applyChange(directory, side) {
    flow = { ...flow, stage: 'done', landing: joinDir(directory, side) };
    patchData({ directory, sideName: side, source: 'this side', ignoredSide: '' });
  }

  function resetToDefault() {
    if (st().setup.data.source === 'default') {
      flow = null;
      app().render();
      return toast('This window already uses the default folder — nothing changed.');
    }
    flow = { ...flow, stage: 'done', landing: DEFAULT_DIR };
    patchData({ directory: DEFAULT_DIR, sideName: '', source: 'default' });
  }

  /* tellClientsToCatchUp: the block on the clipboard, a modal to paste it, then a reload OFFERED. */
  function handover() {
    return `${pasteBlock(flow.landing)}
      <div class="inline"><button type="button" class="btn primary small" data-setup="flow-pasted">I have pasted it</button>
        <button type="button" class="btn small" data-setup="flow-cancel">Close</button></div>`;
  }

  function pasteBlock(landing) {
    if (!app().mcpVersion()) {
      return `<p>This window now reads <code>${esc(landing)}</code>. Install the MCP server, then paste the block it gives you into
        your MCP client — until you do, the server writes where its own entry tells it to.</p>`;
    }
    return `<p><b>This window now reads <code>${esc(landing)}</code>, and the updated MCP config block is on your clipboard.</b></p>
      <pre class="paste">${esc(JSON.stringify({ env: envOf(st().setup.data) }, null, 2))}</pre>
      <p class="from">Paste it into your MCP client and restart it. Until you do, the server it starts keeps writing to the folder its
        own entry names — which is the old one. The path has to be the one that names this folder where the SERVER runs: the same
        drive is reached by a different route from Windows and from WSL. Other windows of this side keep the old folder until they
        are reloaded.</p>`;
  }

  function pasted() {
    const landing = flow.landing || resolvedDir(st().setup.data);
    flow = null;
    app().render();
    toast(`Offered: "Reload this window so everything in it reads ${landing}?" [Reload Window] — the escalation watcher, the consultation watcher and the chat store still hold the old folder until a reload.`);
  }

  /* ---------- Move ---------- */

  function moveBody(d) {
    const done = flow.step >= MOVE_STEPS.length;
    const steps = MOVE_STEPS.map((text, n) => {
      const state = n < flow.step ? 'ok' : n === flow.step && flow.running ? 'warn' : '';
      const word = state === 'ok' ? 'done' : state === 'warn' ? 'running…' : 'waiting';
      return `<li><span class="state ${state || 'idle'}">${word}</span> ${esc(text)}</li>`;
    }).join('');
    const side = sideOf(d);
    const travels = side ? ` The side <b>${esc(side)}</b> travels with the data: it lands in <code>&lt;folder&gt;\\${esc(side)}</code>.` : '';
    return `${folderField()}
      <p class="from">From <code>${esc(flow.from || resolvedDir(d))}</code>.${travels}
        Copied, never moved — the sign-in tokens and the scratch worktrees stay behind.</p>
      ${done ? '' : stoppedTick()}
      <ol class="move-steps">${steps}</ol>
      ${done ? moveDone() : moveButtons()}`;
  }

  function stoppedTick() {
    return `<label class="inline"><input type="checkbox" data-flow="stopped"${flow.stopped ? ' checked' : ''}${flow.running ? ' disabled' : ''}>
        <span>The server is stopped — my AI client is closed, so nothing writes to the database</span></label>
      <p class="from">The extension cannot stop the server: your MCP client owns that process. What the source holds is read when
        you press Move; deleting the old folder later reads it again after you confirm, and refuses if anything was written since.</p>`;
  }

  /* Not `disabled` while unticked: a press says why, which a dead button cannot. */
  function moveButtons() {
    const blocked = !flow.stopped && !flow.running;
    return `<div class="inline"><button type="button" class="btn primary small" data-setup="move-start"${flow.running ? ' disabled' : ''}
        ${blocked ? 'aria-disabled="true" title="Tick the box above first"' : ''}>${flow.running ? 'Moving…' : 'Move'}</button>
      <button type="button" class="btn small" data-setup="flow-cancel"${flow.running ? ' disabled' : ''}>Cancel</button></div>`;
  }

  function moveDone() {
    return `<p><span class="state ok">moved</span> Your data is in <code>${esc(flow.landing)}</code> and reads back the same 212 rounds.
        <code>${esc(flow.from)}</code> still holds the original — delete it from this panel once you are sure.</p>
      ${handover()}`;
  }

  const norm = (p) => p.trim().replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase();
  const inside = (outer, inner) => norm(inner).startsWith(`${norm(outer)}\\`);

  /* The refusals made before anything is touched — the reverse of Change: here a folder holding a history is refused. */
  function moveRefusal(target, d) {
    const t = target.trim();
    if (!t) return 'Choose a folder first.';
    const from = resolvedDir(d);
    const landing = joinDir(t, sideOf(d));
    if (norm(t) === norm(currentDir(d)) || norm(landing) === norm(from)) return 'That is the folder this window already uses.';
    if (inside(from, landing)) {
      return `${landing} is inside ${from}, which is the folder being copied. The copy would land inside its own source, and deleting the old folder afterwards would take the copy with it. Choose a folder outside this one.`;
    }
    if (inside(landing, from)) return `${landing} contains ${from}, the folder being copied. Choose a folder that is not above this one.`;
    if (/history|shared|coai-data-old/i.test(t)) {
      return 'That folder already holds coai.db, sessions/, which means it has a history of its own. Copying into it would write over that history before anything could check it. Choose an empty folder, or a side name inside this one that is not in use.';
    }
    return '';
  }

  function moveStart() {
    if (flow.running) return;
    const d = st().setup.data;
    const problem = moveRefusal(flow.target, d)
      || (flow.stopped ? '' : 'Close your AI client first, so its server stops writing — then tick the box above.');
    if (problem) return setFlow({ problem });
    setFlow({ problem: '', running: true, step: 0, from: resolvedDir(d), landing: joinDir(flow.target.trim(), sideOf(d)) });
    setTimeout(moveTick, 700);
  }

  /* `step` counts finished steps; the window switches folder once the fourth (the switch) is done. */
  function moveTick() {
    if (!flow || flow.kind !== 'move') return;
    const step = flow.step + 1;
    const done = step >= MOVE_STEPS.length;
    flow = { ...flow, step, running: !done };
    if (step === 4) patchData({ directory: flow.target.trim(), source: 'this side', ignoredSide: '' });
    else if (done) finishMove();
    else app().render();
    if (!done) setTimeout(moveTick, 700);
  }

  /* The record is written LAST, when the copy verified and the settings landed — persisted, so it survives a reload. */
  function finishMove() {
    patchData({ movedFrom: flow.from, movedTo: flow.landing, movedOn: today() });
    toast(`Your data is in ${flow.landing} and reads back the same 212 rounds. ${flow.from} still holds the original.`);
  }

  function confirmDeleteOld() {
    const d = st().setup.data;
    const old = d.movedFrom;
    const to = d.movedTo || resolvedDir(d);
    app().confirm({
      title: `Delete ${old}?`, action: 'Delete it', danger: true,
      body: `<p>Its contents were copied to <code>${esc(to)}</code> and read back the same.</p>
        <p>Before you press this: every MCP client must already have been given the new block and restarted. A client still
          configured for this folder will recreate it and write there, and you will be reading one history while it writes another.</p>
        <p class="from">The old folder is read again after you confirm: if anything was added to it or removed from it since the copy,
          nothing is deleted. A file edited in place is not something this can see.</p>
        <p class="from">This cannot be undone, and it is the last copy of anything the move did not take — the sign-in tokens and the
          scratch worktrees are deliberately not copied.</p>`,
      onConfirm: () => {
        patchData({ movedFrom: '', movedTo: '', movedOn: '' });
        toast(`${old} is gone. Your data is in ${to}.`);
      },
    });
  }

  function patchData(change) {
    patchSetup({ data: { ...st().setup.data, ...change } });
  }

  /* ---------- This side ---------- */

  function sidePage() {
    const s = st().setup;
    return `<p class="lead">One machine can hold several working environments — a Windows window and each WSL distro or remote
        host. VS Code hands them one settings file; this is what keeps them apart.</p>
      <div class="plan-grid">${perSidePanel(s)}${transferPanel(s)}</div>`;
  }

  function perSidePanel(s) {
    const sides = [`<b>${esc(s.side)}</b> — this window`, ...s.otherSides.map(esc)].map((x) => `<li>${x}</li>`).join('');
    return `<section class="panel"><h3>Separate settings for each side</h3>
      <label class="inline"><input type="checkbox" data-setup-field="perSide"${s.perSide ? ' checked' : ''}> Separate settings for each side${help('perSideSettings')}</label>
      <p class="from">${s.perSide
        ? `This side is <b>${esc(s.side)}</b>. It keeps its own models, CLI paths, proxies and vault key; your text size and help language stay shared.`
        : 'It shares its settings with every other side.'}</p>
      <div class="settings-grid"><div class="field"><span>Kept per side</span><ul class="tight"><li>Models and their connections</li><li>CLI paths, endpoints</li><li>The vault key</li></ul></div>
        <div class="field"><span>Always shared</span><ul class="tight"><li>Text size and brightness</li><li>Help language</li><li>Chat prompt presets</li></ul></div></div>
      <p class="from">The data folder and its side name are per side ALWAYS, whatever this switch says — the same NAS is <code>Z:\\coai</code>
        in a Windows window and <code>/mnt/z/coai</code> in a WSL one.</p>
      <div class="field" style="margin-top: 8px"><span>Sides seen on this machine</span><ul class="tight">${sides}</ul></div></section>`;
  }

  /* configTransfer reads the BASE layer only: with sides separated, a side's own values never reach the file. */
  function transferPanel(s) {
    const perSide = s.perSide
      ? '<p class="callout warn">Export writes the shared settings — this side\'s own models and paths are not in the file.</p>' : '';
    return `<section class="panel"><h3>Move your settings to another machine</h3>
      <div class="inline"><button type="button" class="btn small" data-setup="export">Export settings…</button>
        <button type="button" class="btn small" data-setup="import">Import settings…</button></div>
      ${perSide}
      <div class="settings-grid" style="margin-top: 10px"><div class="field"><span>Goes into the file</span><ul class="tight">
        <li>Every shared setting that differs from its default</li><li>The prompt texts you changed</li></ul></div>
        <div class="field"><span>Never goes into it</span><ul class="tight"><li>Keys and the vault key</li>
        <li>This machine's paths: CLI paths, the data folder</li><li>Team server sign-ins</li></ul></div></div>
      <p class="from">An import shows what it will change and asks once. This machine's CLI paths are kept.</p></section>`;
  }

  const PAGES = { keys: keysPage, team: teamPage, mcp: mcpPage, side: sidePage };

  /* ---------- events: each returns true when it handled the event ---------- */

  const ACTIONS = {
    'toggle-creds': (t) => { const f = document.getElementById('creds-key'); const show = f.type === 'password'; f.type = show ? 'text' : 'password'; t.textContent = show ? 'Hide' : 'Show'; },
    'check-vault': () => { patchSetup({ vaultAnswered: true }); toast(`The vault answered: ${VAULT_KEYS.length} key names.`); },
    'cli-open': (t) => cliOpen(groupOf(t)),
    'cli-install': (t) => cliInstall(groupOf(t)),
    'cli-update': (t) => cliUpdate(groupOf(t)),
    'add-server': () => { adding = { name: '', url: '', problem: '', checking: false, unreachable: false }; app().render(); },
    'add-server-cancel': () => { adding = null; app().render(); },
    'add-server-commit': () => addServer(),
    'add-server-anyway': () => commitServer(new URL(adding.url.trim()), false),
    'add-model': (t) => app().openAddFor(serverOfTarget(t)),
    'sign-in': (t) => signIn(serverOfTarget(t)),
    'sign-out': (t) => patchServer(serverOfTarget(t), { signedIn: false }),
    'recheck-server': (t) => recheck(serverOfTarget(t)),
    'remove-server': (t) => confirmRemoveServer(serverOfTarget(t)),
    'check-mcp': () => patchSetup({ mcp: { ...st().setup.mcp, checked: 'just now' } }),
    'install-mcp': () => installMcp('install'),
    'update-mcp': () => installMcp('update'),
    'copy-config': (t) => copy(configBlock(t.dataset.client), `Config block for ${t.dataset.client} copied.`),
    'copy-snippet': () => copy('<!-- coai-snippet v5 --> ## Multi-model review gate (ConnectOtherAIs) …', 'Gate instructions copied.'),
    'change-dir': () => { flow = { kind: 'change', stage: 'pick', pick: 'choose', target: '', side: 'windows', problem: '' }; app().render(); },
    'move-dir': () => { flow = { kind: 'move', target: '', problem: '', step: 0, running: false, stopped: false }; app().render(); },
    browse: () => {
      const picked = window.prompt('In the extension this is a folder picker. Type a folder:', flow.target || 'E:\\coai-data');
      if (picked !== null) setFlow({ target: picked, problem: '' });
    },
    'change-apply': () => changeApply(),
    'change-continue': () => continueHistory(),
    'change-fresh': () => applyChange(flow.target.trim(), cleanSide(flow.side)),
    'change-back': () => setFlow({ stage: 'pick' }),
    'move-start': () => moveStart(),
    'flow-pasted': () => pasted(),
    'flow-cancel': () => { flow = null; app().render(); },
    'delete-old': () => confirmDeleteOld(),
    export: () => toast(`${SIMULATED}a save dialog for coai-settings.json.${st().setup.perSide ? ' It writes the shared settings only.' : ''}`),
    import: () => toast(`${SIMULATED}a file picker, then shows what it will change.`),
  };

  function onClick(e) {
    if (e.target.closest('[data-goto-models]')) { e.preventDefault(); location.hash = 'models'; return true; }
    const t = e.target.closest('[data-setup]');
    if (!t) return false;
    ACTIONS[t.dataset.setup]?.(t);
    return true;
  }

  const FIELD_CHANGES = {
    perSide: (t) => patchSetup({ perSide: t.checked }),
    credsKey: (t) => patchSetup({ credsKey: t.value, vaultAnswered: false }),
    demoAgyMissing: (t) => { agyMissing = t.checked; app().render(); },
    demoSource: (t) => { demoOpen = true; patchData({ source: t.value }); },
    demoIgnoredSide: (t) => { demoOpen = true; patchData(t.checked ? { ignoredSide: 'windows', source: 'default' } : { ignoredSide: '' }); },
  };

  const FLOW_CHANGES = {
    pick: (t) => setFlow({ pick: t.value, problem: '' }),
    stopped: (t) => setFlow({ stopped: t.checked, problem: '' }),
    adopt: (t) => { flow = { ...flow, adopt: t.value }; },
  };

  function onChange(e) {
    const t = e.target;
    const field = FIELD_CHANGES[t.dataset.setupField];
    if (field) { field(t); return true; }
    const step = flow && FLOW_CHANGES[t.dataset.flow];
    if (step) { step(t); return true; }
    if (t.dataset.flow) return true;
    return Boolean(t.dataset.addServer);
  }

  /* Typing never re-renders, so the caret stays where it is. */
  function onInput(e) {
    const t = e.target;
    if (flow && (t.dataset.flow === 'target' || t.dataset.flow === 'side')) { flow = { ...flow, [t.dataset.flow]: t.value, problem: '' }; return true; }
    const field = t.dataset.addServer;
    if (!field || !adding) return false;
    adding = { ...adding, [field]: t.value };
    return true;
  }

  function patchSetup(change) {
    app().setState({ ...st(), setup: { ...st().setup, ...change } });
    app().render();
  }

  async function copy(text, said) {
    try { await navigator.clipboard.writeText(text); toast(said); } catch { toast(`${said.replace(' copied.', '')} — the browser refused the clipboard here; the extension copies it.`); }
  }

  window.COAI_PAGES = { ...window.COAI_PAGES, setup: { html, onClick, onChange, onInput } };
})();
