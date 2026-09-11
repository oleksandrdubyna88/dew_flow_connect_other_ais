# Post-deploy checks — ConnectOtherAIs

Per [`.agents/conventions/common/post-deploy-checks.md`](.agents/conventions/common/post-deploy-checks.md).

**Two of the three halves have no HTTP surface, and the third is nothing but one.** The MCP server
speaks JSON-RPC over stdio and the extension speaks to it as a subprocess: neither is a request that
can be written down, which the contracts rule says in as many words. The **Team server is an HTTP
service**, and its contract suite is [`http/`](http/README.md) — `node http/run-contracts.mjs`, every
route as a request a client actually sends. Three items below reach it over the wire for that reason.

*(This paragraph claimed the repository had no HTTP surface at all, and no `http/` suite, while sitting
at the top of a file containing three HTTP checks and one directory over from the suite itself. Written
before the Team server existed and never revisited — CodeRabbit, on the pull request that made those
three checks actually assert.)*

What ships is **three** artefacts on three clocks, to three different places — the extension to the
Marketplace, the MCP binary to its own tag, the server to one VM — and most of the failures below are
one of them arriving wrong.

Target: the released **extension** version — `--target 0.29.3`. The MCP binary ships on its own tag and its own number, so item 1 reads `MCP_VERSION` (`mcp-v<version>`) rather than the target.

Last verified: 2026-09-11 · extension 0.33.1 / mcp 0.18.17 / **server 0.5.7** · all five automated items PASS. **Item 12 observed for the first time, and it is the reason this release exists**: the release script's canary ran one real review per configured vendor on the box — `codex: done`, `antigravity: done`, `claude: done` — so the INSTALLED claude (2.1.261, not the 2.1.258 the flags were read against) accepts both halves of the confinement: the eleven `--disallowedTools` names and an allowlisted environment. Until that ran, both were asserted as SENT and nothing had observed them being taken.

Three deploys of 0.5.7 were REJECTED before this one, each rolled back by the canary in under a second, and none of them touched the serving release — `/api/health` answered 0.5.6 throughout, and the workflow's verify step correctly reported *unchanged* rather than rolling a healthy server back a version for a deploy that never landed. The cause was not this change: the claude account was on hold (`Your account is on hold and can't use Claude Code`), which the launch reports as `exited 1 without saying why` because the CLI puts it in its JSON envelope rather than on stderr. Proved not to be the confinement by running the identical argv on the box with the FULL environment and getting the same `api_error`, zero tokens, zero cost — then fixed by signing the slot in again with `HOME` pointed at `/opt/coai/data/accounts/claude/a`, which is the thing that is easy to get wrong: a sign-in without it lands in `/root/.claude`, where the server never looks.

Item 10 passed for the first time on its own terms. It was rewritten earlier the same day, having been a bare `curl -fsS .../api/health` that exits 0 for any server that answers — it reported **PASS** against a box serving 0.5.6 with `SERVER_VERSION=0.5.7` set, which is precisely the failure the row exists to catch.

**A failed matrix leg now leaves a DRAFT, not a partial release (2026-09-08).** Every release line
creates its release as a draft and publishes it from a completeness job that checks each expected
asset BY NAME. So a red release run means *nothing was published*, and item 1 below answers about a
release that never became visible — `gh release view` still finds a draft, and that is the state to
expect after a failure. Before this, the same failure left five platforms of six on a PUBLISHED
release and the sixth answering 404 to whoever was installing on it; it happened on `mcp-v0.16.0`
and again on `mcp-v0.18.13`, and both times a person found it rather than CI.

**A release stuck as a draft is recovered by re-running, never by publishing it blind.** The
completeness job is the only thing that publishes, so if it never ran (a leg failed) or failed
itself, the release stays a draft. Use *Re-run failed jobs* — the completeness job depends on the
matrix, so re-running the failed leg re-runs the check and the publish with it. If it is the
CHECK that is failing, read what it says is missing rather than publishing by hand: it names the
asset, and a release published without it is the exact 404 this whole shape exists to prevent.
The manual last resort, once you have looked: `gh release edit <tag> --draft=false`.

**A slow `npm ci` is not a hung release, and cancelling one costs a re-run.** Measured here on
extension-v0.29.2: the step sat at seven minutes against a whole-job history of 2m33s, was cancelled
as hung, and the re-attempt took 7m26s and published cleanly. The registry is simply slower some
mornings. Read the step's own elapsed time against the JOB's history before acting, and prefer waiting
— the only thing a cancel buys is another cold `npm ci`.

**Item 12 spends money, on purpose, and that is why it is manual (2026-09-11).** Since the server
launches every reviewer confined, each claude launch sends a `--disallowedTools` list read off the
CLI installed HERE (2.1.258), and every launch starts from an allowlisted environment rather than the
server's own. Whether the box's CLI accepts that argv, and whether each vendor can still start without
whatever `/etc/coai-server.env` used to hand it, is a fact about the installed binaries, and a green
suite asserts only what is sent. One short review per configured vendor is the only instrument that
can see it — so it runs when a person decides to spend it, never on every pass of this list.

**The marketplace takes minutes, and item 2 is not a failure before it does.** Measured on 0.26.2: `vsce` reported *"Published remsoftdev.connect-other-ais v0.26.2"* while the gallery query kept answering `0.26.1` for a further four and a half minutes, then flipped. Read the publish step's own log before treating a red item 2 as a bad release — the two answers disagree by design for a while.

| # | What a person loses if this is broken | Check | Auto |
|---|---|---|---|
| 1 | A user on one platform presses Install and finds **no asset for it** — not an error, an absence, on the newest version, with five siblings present to prove the release "worked". Measured here on 2026-09-03; the tag had to be burned | `node -e "const{execFileSync}=require('child_process');const a=JSON.parse(execFileSync('gh',['release','view','mcp-v'+process.env.MCP_VERSION,'--json','assets'],{encoding:'utf8'})).assets.map(x=>x.name).join(' ');const rids=['linux-x64','linux-arm64','win-x64','win-arm64','osx-x64','osx-arm64'];const missing=rids.filter(r=>!a.includes(r));console.log(missing.length?'missing: '+missing.join(', '):'all six RIDs present');process.exitCode=+(missing.length?1:0)"` | auto |
| 2 | Everyone installs an extension whose JavaScript is three versions old: it installs cleanly, reports success, and behaves exactly as before while somebody restarts the editor looking for a change that never shipped | `node -e "fetch('https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json;api-version=3.0-preview.1'},body:JSON.stringify({filters:[{criteria:[{filterType:7,value:'remsoftdev.connect-other-ais'}]}],flags:914})}).then(r=>r.json()).then(j=>{const v=j.results[0].extensions[0].versions[0].version;console.log('marketplace serves',v);process.exitCode=+(v===process.env.TARGET?0:1)})"` | auto |
| 3 | The review gate answers with **fewer reviewers than it claims**: a vendor CLI whose sign-in expired still exits 0 on `--version`, so a probe built on that cannot see it — the retirement only surfaces at sign-in, and a degraded round looks like a completed one | Open the panel and read what `providers` reports: every provider you expect must be listed AND say what it authenticates as. A missing one is silent | manual |
| 5 | A person signs in to a Team server, the panel says they are signed in, and **every review still exits 77** — because the extension wrote the token to one filename and the shim looked for another. The two derive that path independently, in two languages, and a green build proves only that each agrees with itself | With the extension installed and a Team server signed in, list `<dataDir>/servers/` and check the filename against what the shim computes: `node -e "const{createHash}=require('crypto');const u=process.env.SERVER_URL.trim().replace(/\/+$/,'').toLowerCase();console.log(createHash('sha256').update(u).digest('hex').slice(0,16)+'.token')"`. A mismatch is this item failing, and `shared/team-server-url-vectors.json` is what should have caught it | manual |
| 6 | A reviewer added from a Team server is refused by that server as a vendor it **does not offer** — the row id travelled where the server's own vendor name should have. It reads exactly like a typo, so the first hour goes into the spelling | Add a reviewer from a Team server and run one round. The round must produce an answer; a `does not offer` note in the panel is this item failing | manual |
| 7 | The Team server is **up but refuses everything**: `/api/health` answers and every other route says `403 HTTPS required.` — because `UseForwardedHeaders` consumes `X-Forwarded-Proto` from a TRUSTED proxy and the gate read the raw header. Configuring `Coai:TrustedProxies` CORRECTLY was what broke it, which is why it survived review and 165 tests | `node -e "const b='https://coai.remsoft.dev/api';const t=AbortSignal.timeout(15000);Promise.all([fetch(b+'/client-config',{signal:t}),fetch(b+'/catalog',{signal:t})]).then(async([c,k])=>{const body=(await c.text()).trim();console.log('client-config',c.status,body.slice(0,90),'- catalog',k.status);if(c.status!==200){console.log('client-config answered '+c.status);process.exitCode=1;return}let j;try{j=JSON.parse(body)}catch{console.log('client-config did not answer JSON');process.exitCode=1;return}if(typeof j.microsoftScope!=='string'){console.log('client-config carries no scope');process.exitCode=1}if(!Array.isArray(j.providers)){console.log('client-config carries no provider list');process.exitCode=1}if(k.status!==401){console.log('catalog answered '+k.status+', not 401 - a 403 here is the proxy bug this item exists for');process.exitCode=1}}).catch(e=>{console.log('the server did not answer: '+e.message);process.exitCode=1})"` — BOTH routes, because the second half of this item used to be prose the runner never executed: `curl -fsS` catches the client-config side on its own, and nothing at all was asking `/api/catalog` whether it answers **401** (no token) rather than the **403** that is this item's actual symptom. The body is PARSED and its two contract fields required, since a 200 beginning with a brace is not the same as the scope JSON (CodeRabbit) | auto |
| 8 | The server runs, and **none of its vendors can review**: a CLI that the image or the host updated no longer starts, and nothing says so until a round fails halfway through | `curl` `/api/catalog` with a session token and check all three vendors report a version — the server probes them at startup, so a blank version is a CLI that did not run | manual |
| 4 | The extension spawns a **stale** `coai-mcp` — the binary beside a fresh extension is the one that was published last, not the one that was just built | Check the packaged binary's version against the release you just made, from inside the installed extension folder rather than from the repository | manual |
| 9 | A Team server release ships **five of six platforms**, exactly as item 1 describes for the MCP binary — and the one that goes missing is the one the live host installs, so the next deploy stops at its preflight with nothing to download. Applies from `server-v0.5.5`, the first tag that publishes binaries at all | `node -e "const{execFileSync}=require('child_process');const v=process.env.SERVER_VERSION;const a=JSON.parse(execFileSync('gh',['release','view','server-v'+v,'--json','assets'],{encoding:'utf8'})).assets.map(x=>x.name);const rids=['linux-x64','linux-arm64','win-x64','win-arm64','osx-x64','osx-arm64'];const missing=rids.filter(r=>!a.includes('coai-server-'+v+'-'+r+(r.startsWith('win-')?'.zip':'.tar.gz')));console.log(missing.length?'missing: '+missing.join(', '):'all six RIDs present');process.exitCode=+(missing.length?1:0)"` | auto |
| 10 | The deploy workflow reports success and the box serves **the version it served before** — the swap silently did not happen, and the number everyone quotes comes from a binary nobody can trace. This is the failure this whole line exists for: 0.5.5 was on the internet, built by hand, from a commit no tag names | `node -e "const v=process.env.SERVER_VERSION;if(!v){console.log('SERVER_VERSION is not set');process.exit(1)}fetch('https://coai.remsoft.dev/api/health',{headers:{'Cache-Control':'no-cache'},signal:AbortSignal.timeout(15000)}).then(async r=>{const body=(await r.text()).trim();console.log(r.status,body.slice(0,120));if(r.status!==200){console.log('health answered '+r.status);process.exitCode=1;return}let j;try{j=JSON.parse(body)}catch{console.log('health did not answer JSON');process.exitCode=1;return}if(j.ok!==true){console.log('health says ok is not true');process.exitCode=1;return}if(j.version!==v){console.log('the box serves '+j.version+', not '+v);process.exitCode=1}}).catch(e=>{console.log('the server did not answer: '+e.message);process.exitCode=1})"` — it COMPARES, which is the whole item: the old command was a bare `curl -fsS .../api/health`, and a bare fetch exits 0 for any server that answers, including the one that was already there. Measured 2026-09-11 while waiting for the 0.5.7 deploy: this row passed against a box serving 0.5.6 with `SERVER_VERSION=0.5.7` set. The status and `ok` are checked BEFORE the version, because `fetch` resolves for an HTTP error and a 500 page carrying the right number is not a healthy release (CodeRabbit). `no-cache`, because a proxy that answered a minute ago could hand back either version's body. The host half is still a person's: `systemd-release.sh --list` must show `bin` pointing at a `releases/<that version>-<stamp>` directory | auto |
| 11 | The installed extension reports the gate missing, or the installed MCP reviews only bootstrap text, after the repository moved to shared instructions | Open a neutral-layout checkout with the installed extension: its snippet must be current, and an older local copy must be reported as older. Run a code review using the installed MCP; its log must name canonical rule files and explicit omissions or missing mounts. Check the installed artifact, not the source build | manual |
| 12 | **Every review of one vendor fails before it starts** — `not_started` or `non_zero_exit`, on each — because the box's `claude` does not know a name in the `--disallowedTools` list this release sends, or a vendor cannot start on the allowlisted environment without something the server's own environment used to supply. Each half of the confinement is asserted in the suite as SENT; only the installed CLI can say whether it is accepted, and it says so on every review of that vendor rather than once | `node -e "(async()=>{const b='https://coai.remsoft.dev/api',h={Authorization:'Bearer '+process.env.COAI_SESSION_TOKEN,'Content-Type':'application/json'};const j=async r=>{if(!r.ok)throw new Error(r.status+' '+await r.text());return r.json()};let bad=0;for(const v of (await j(await fetch(b+'/catalog',{headers:h}))).vendors){const t0=Date.now(),stop=t0+300000;const a=await j(await fetch(b+'/reviews',{method:'POST',headers:h,body:JSON.stringify({vendor:v.id,model:v.models[0],role:'Architecture',prompt:'Reply with the word OK and nothing else.',timeoutSeconds:120})}));let s;do{if(Date.now()>stop){console.log(v.id,'TIMED OUT after',Math.round((Date.now()-t0)/1000)+'s, last status',s&&s.status);bad++;break}s=await j(await fetch(b+'/reviews/'+a.id+'?wait=25',{headers:h}));console.log(v.id,Math.round((Date.now()-t0)/1000)+'s',s.status)}while(s.status==='queued'\\|\\|s.status==='running');if(!s\|\|s.status==='queued'\\|\\|s.status==='running')continue;const dead=s.failure==='not_started'\\|\\|s.failure==='non_zero_exit';console.log(v.id,s.status,s.failure,s.reason);if(dead)bad++}process.exitCode=bad?1:0})()"` — with a session token in `COAI_SESSION_TOKEN`; one real review per configured vendor, polled to its end. It prints each vendor's status on every poll with the seconds elapsed, so a check that is alive looks different from one that is hung, and gives up per vendor after five minutes with a named timeout rather than waiting for ever — asked for on the code round, because the operator reading this is deciding whether to roll back. It fails on the two terminal states that mean the CLI never ran; `unparseable_by_vendor` on a one-word prompt is not this item | manual |

## When item 12 fails: the way back, written down before it is needed

Item 12 is the only check whose failure means **every review of a vendor is dead**, and it is the one
most likely to be met at an awkward hour, so the recovery belongs here rather than in somebody's head.
Asked for on the plan round of the confinement change (codex, Major): a deployment whose only guard is
an observation needs a written way back.

The failure looks like `not_started` or `non_zero_exit` on every review of one vendor, with the
vendor's own words in `reason`. Two causes and one answer:

1. **The installed CLI does not know a name in `--disallowedTools`.** Only `claude` receives that list;
   codex and antigravity are untouched by the confinement's tool half, so a failure on all three is the
   environment rather than the flags.
2. **A vendor cannot start on the allowlisted environment**, because it needed something the server's
   own environment used to supply. `ProcessEnvironment.Passthrough` is the list; `reason` usually names
   what is missing, and a Node CLI with no `HOME` says so loudly.

**The way back is the previous release, not a code change.** `deploy/systemd-release.sh --rollback`
pops one deployment off the retained trail and restarts — seconds, no build — and `--list` shows what
is there if you would rather name one by hand. The version `/api/health` reports is how you know which
is serving. Roll back FIRST and diagnose after: the box is shared, and a vendor that cannot review is
the whole team's gate down.

**And be clear about what a rollback costs here, because it is not nothing.** This release is the one
that introduces confinement, so the previous one is the server as the audit of 2026-09-09 found it: an
employee's arbitrary prompt running with the box's own environment and, for `claude`, a shell, on the
machine that holds every shared vendor account. Rolling back is still the right first move — a gate
nobody can use is worse, and the exposure needs a colleague who means harm — but it is a return to a
known state rather than to a safe one, and the fix is to go forward again with the flag the installed
CLI accepts rather than to sit there.

## Why item 1 is first

Because a partial publish is the worst of the three sibling failures
[`development-workflow.md`](.agents/conventions/common/development-workflow.md) records: every signal
is green **and** the artefact exists, so there is nothing to notice. The other two — an artefact never
rebuilt, an artefact never deployed — at least leave something behind that looks wrong.

## Running it

```bash
gh auth status                                   # item 1 reads the release through gh
export MCP_VERSION=0.15.0                        # the binary's own tag: mcp-v<version>
export SERVER_VERSION=0.5.5                      # the Team server's own tag: server-v<version>
node .agents/conventions/tools/post-deploy-check.mjs --target 0.29.3
```

`TARGET` here is a **version**, not a URL: what is being checked is what a user receives, and both
places a user receives it from are addressed by name rather than by host.

**Three numbers, on purpose.** The tags are `extension-v<x>`, `mcp-v<y>` and `server-v<z>` and they move
independently — a release of one is not a release of the other. A checklist that assumed one version
would check the wrong artefact half the time, which is how this item was written wrong the first time
and caught by running it: `gh release view v0.26.1` answered *release not found*.


## Local shared-instructions canary — 2026-09-10

Partial verification: extension **0.32.3**, MCP **0.18.16-sharedrules.20260910**.
Item 4 passes: the installed globalStorage binary reports that version and its SHA matches
`research/shared-rules-adoption-smoke.json`. Five real stdio contract cases pass against it.
The installed extension bundle matches the inspected VSIX, including all canonical gate-body
bytes. Item 11's manual panel scenario and an existing editor host reloading are still open;
no existing agent/editor process was stopped. Marketplace and Team-server items are not
claims about this local-only installation.

Rollback uses existing artifacts, not a rebuild: install
`artifacts/shared-rules/before-install/connect-other-ais-0.31.18.vsix` with the VS Code CLI
(`code --install-extension <that-file> --force`), and restore the preserved MCP executable
from `artifacts/shared-rules/before-install/coai-mcp.exe` using the same atomic replacement.
The identical SQLite library was left in place. These task artifacts are local and ignored;
public release 0.31.18 and MCP release 0.18.15 remain the durable prior release sources.
