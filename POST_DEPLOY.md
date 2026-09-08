# Post-deploy checks — ConnectOtherAIs

Per [`.claude/rules/shared/common/post-deploy-checks.md`](.claude/rules/shared/common/post-deploy-checks.md).

**This repository has no HTTP surface**, so it has no `http/` suite: the MCP server speaks JSON-RPC
over stdio and the extension speaks to it as a subprocess. Neither is a request that can be written
down — which the contracts rule says in as many words. What it does have is two artefacts that ship on
their own clocks, to two different places, and every failure below is one of them arriving wrong.

Target: the released **extension** version — `--target 0.29.3`. The MCP binary ships on its own tag and its own number, so item 1 reads `MCP_VERSION` (`mcp-v<version>`) rather than the target.

Last verified: 2026-09-04 · extension 0.29.5 / mcp 0.16.0 · item 1 PASS on the SECOND tag — the first mcp-v0.16.0 shipped five RIDs and no win-x64, because a new test starved a two-core runner and a real reviewer subprocess died beside it; the tag was burned and re-cut. Item 2 PASS. Item 4 done by hand: the binary beside the installed extension was replaced from the release asset after its checksum was verified, and answers `coai-mcp 0.16.0`.

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

**The marketplace takes minutes, and item 2 is not a failure before it does.** Measured on 0.26.2: `vsce` reported *"Published remsoftdev.connect-other-ais v0.26.2"* while the gallery query kept answering `0.26.1` for a further four and a half minutes, then flipped. Read the publish step's own log before treating a red item 2 as a bad release — the two answers disagree by design for a while.

| # | What a person loses if this is broken | Check | Auto |
|---|---|---|---|
| 1 | A user on one platform presses Install and finds **no asset for it** — not an error, an absence, on the newest version, with five siblings present to prove the release "worked". Measured here on 2026-09-03; the tag had to be burned | `node -e "const{execFileSync}=require('child_process');const a=JSON.parse(execFileSync('gh',['release','view','mcp-v'+process.env.MCP_VERSION,'--json','assets'],{encoding:'utf8'})).assets.map(x=>x.name).join(' ');const rids=['linux-x64','linux-arm64','win-x64','win-arm64','osx-x64','osx-arm64'];const missing=rids.filter(r=>!a.includes(r));console.log(missing.length?'missing: '+missing.join(', '):'all six RIDs present');process.exitCode=+(missing.length?1:0)"` | auto |
| 2 | Everyone installs an extension whose JavaScript is three versions old: it installs cleanly, reports success, and behaves exactly as before while somebody restarts the editor looking for a change that never shipped | `node -e "fetch('https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json;api-version=3.0-preview.1'},body:JSON.stringify({filters:[{criteria:[{filterType:7,value:'remsoftdev.connect-other-ais'}]}],flags:914})}).then(r=>r.json()).then(j=>{const v=j.results[0].extensions[0].versions[0].version;console.log('marketplace serves',v);process.exitCode=+(v===process.env.TARGET?0:1)})"` | auto |
| 3 | The review gate answers with **fewer reviewers than it claims**: a vendor CLI whose sign-in expired still exits 0 on `--version`, so a probe built on that cannot see it — the retirement only surfaces at sign-in, and a degraded round looks like a completed one | Open the panel and read what `providers` reports: every provider you expect must be listed AND say what it authenticates as. A missing one is silent | manual |
| 5 | A person signs in to a Team server, the panel says they are signed in, and **every review still exits 77** — because the extension wrote the token to one filename and the shim looked for another. The two derive that path independently, in two languages, and a green build proves only that each agrees with itself | With the extension installed and a Team server signed in, list `<dataDir>/servers/` and check the filename against what the shim computes: `node -e "const{createHash}=require('crypto');const u=process.env.SERVER_URL.trim().replace(/\/+$/,'').toLowerCase();console.log(createHash('sha256').update(u).digest('hex').slice(0,16)+'.token')"`. A mismatch is this item failing, and `shared/team-server-url-vectors.json` is what should have caught it | manual |
| 6 | A reviewer added from a Team server is refused by that server as a vendor it **does not offer** — the row id travelled where the server's own vendor name should have. It reads exactly like a typo, so the first hour goes into the spelling | Add a reviewer from a Team server and run one round. The round must produce an answer; a `does not offer` note in the panel is this item failing | manual |
| 7 | The Team server is **up but refuses everything**: `/api/health` answers and every other route says `403 HTTPS required.` — because `UseForwardedHeaders` consumes `X-Forwarded-Proto` from a TRUSTED proxy and the gate read the raw header. Configuring `Coai:TrustedProxies` CORRECTLY was what broke it, which is why it survived review and 165 tests | `curl -fsS https://coai.remsoft.dev/api/client-config` must return the scope JSON, not `HTTPS required.` — and `/api/catalog` must answer **401** (no token), never 403 | auto |
| 8 | The server runs, and **none of its vendors can review**: a CLI that the image or the host updated no longer starts, and nothing says so until a round fails halfway through | `curl` `/api/catalog` with a session token and check all three vendors report a version — the server probes them at startup, so a blank version is a CLI that did not run | manual |
| 4 | The extension spawns a **stale** `coai-mcp` — the binary beside a fresh extension is the one that was published last, not the one that was just built | Check the packaged binary's version against the release you just made, from inside the installed extension folder rather than from the repository | manual |
| 9 | A Team server release ships **five of six platforms**, exactly as item 1 describes for the MCP binary — and the one that goes missing is the one the live host installs, so the next deploy stops at its preflight with nothing to download. Applies from `server-v0.5.5`, the first tag that publishes binaries at all | `node -e "const{execFileSync}=require('child_process');const v=process.env.SERVER_VERSION;const a=JSON.parse(execFileSync('gh',['release','view','server-v'+v,'--json','assets'],{encoding:'utf8'})).assets.map(x=>x.name);const rids=['linux-x64','linux-arm64','win-x64','win-arm64','osx-x64','osx-arm64'];const missing=rids.filter(r=>!a.includes('coai-server-'+v+'-'+r+(r.startsWith('win-')?'.zip':'.tar.gz')));console.log(missing.length?'missing: '+missing.join(', '):'all six RIDs present');process.exitCode=+(missing.length?1:0)"` | auto |
| 10 | The deploy workflow reports success and the box serves **the version it served before** — the swap silently did not happen, and the number everyone quotes comes from a binary nobody can trace. This is the failure this whole line exists for: 0.5.5 was on the internet, built by hand, from a commit no tag names | `curl -fsS https://coai.remsoft.dev/api/health` must report `SERVER_VERSION`, and `systemd-release.sh --list` on the host must show `bin` pointing at a `releases/<that version>-<stamp>` directory | auto |

## Why item 1 is first

Because a partial publish is the worst of the three sibling failures
[`development-workflow.md`](.claude/rules/shared/common/development-workflow.md) records: every signal
is green **and** the artefact exists, so there is nothing to notice. The other two — an artefact never
rebuilt, an artefact never deployed — at least leave something behind that looks wrong.

## Running it

```bash
gh auth status                                   # item 1 reads the release through gh
export MCP_VERSION=0.15.0                        # the binary's own tag: mcp-v<version>
export SERVER_VERSION=0.5.5                      # the Team server's own tag: server-v<version>
node .claude/rules/shared/tools/post-deploy-check.mjs --target 0.29.3
```

`TARGET` here is a **version**, not a URL: what is being checked is what a user receives, and both
places a user receives it from are addressed by name rather than by host.

**Three numbers, on purpose.** The tags are `extension-v<x>`, `mcp-v<y>` and `server-v<z>` and they move
independently — a release of one is not a release of the other. A checklist that assumed one version
would check the wrong artefact half the time, which is how this item was written wrong the first time
and caught by running it: `gh release view v0.26.1` answered *release not found*.
