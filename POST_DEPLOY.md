# Post-deploy checks — ConnectOtherAIs

Per [`.claude/rules/shared/common/post-deploy-checks.md`](.claude/rules/shared/common/post-deploy-checks.md).

**This repository has no HTTP surface**, so it has no `http/` suite: the MCP server speaks JSON-RPC
over stdio and the extension speaks to it as a subprocess. Neither is a request that can be written
down — which the contracts rule says in as many words. What it does have is two artefacts that ship on
their own clocks, to two different places, and every failure below is one of them arriving wrong.

Target: the released **extension** version — `--target 0.29.1`. The MCP binary ships on its own tag and its own number, so item 1 reads `MCP_VERSION` (`mcp-v<version>`) rather than the target.

Last verified: 2026-09-03 · extension 0.29.0 / mcp 0.14.0 · item 1 PASS — mcp-v0.14.0 carries all six RID assets. Item 2 was RED at the moment of checking and that is the documented lag, not a bad release: the publish step logged "Published remsoftdev.connect-other-ais v0.29.0" while the gallery still answered 0.28.0, exactly as measured on 0.26.2 (four and a half minutes). Items 3 and 4 are owed on the machine: read `providers` in the panel, and check the packaged binary's version from inside the installed extension — which can now be asked of the binary itself, since `coai-mcp --version` exists as of 0.12.3.

**The marketplace takes minutes, and item 2 is not a failure before it does.** Measured on this release: `vsce` reported *"Published remsoftdev.connect-other-ais v0.26.2"* while the gallery query kept answering `0.26.1` for a further four and a half minutes, then flipped. Read the publish step's own log before treating a red item 2 as a bad release — the two answers disagree by design for a while.

| # | What a person loses if this is broken | Check | Auto |
|---|---|---|---|
| 1 | A user on one platform presses Install and finds **no asset for it** — not an error, an absence, on the newest version, with five siblings present to prove the release "worked". Measured here on 2026-09-03; the tag had to be burned | `node -e "const{execFileSync}=require('child_process');const a=JSON.parse(execFileSync('gh',['release','view','mcp-v'+process.env.MCP_VERSION,'--json','assets'],{encoding:'utf8'})).assets.map(x=>x.name).join(' ');const rids=['linux-x64','linux-arm64','win-x64','win-arm64','osx-x64','osx-arm64'];const missing=rids.filter(r=>!a.includes(r));console.log(missing.length?'missing: '+missing.join(', '):'all six RIDs present');process.exitCode=+(missing.length?1:0)"` | auto |
| 2 | Everyone installs an extension whose JavaScript is three versions old: it installs cleanly, reports success, and behaves exactly as before while somebody restarts the editor looking for a change that never shipped | `node -e "fetch('https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery',{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json;api-version=3.0-preview.1'},body:JSON.stringify({filters:[{criteria:[{filterType:7,value:'remsoftdev.connect-other-ais'}]}],flags:914})}).then(r=>r.json()).then(j=>{const v=j.results[0].extensions[0].versions[0].version;console.log('marketplace serves',v);process.exitCode=+(v===process.env.TARGET?0:1)})"` | auto |
| 3 | The review gate answers with **fewer reviewers than it claims**: a vendor CLI whose sign-in expired still exits 0 on `--version`, so a probe built on that cannot see it — the retirement only surfaces at sign-in, and a degraded round looks like a completed one | Open the panel and read what `providers` reports: every provider you expect must be listed AND say what it authenticates as. A missing one is silent | manual |
| 4 | The extension spawns a **stale** `coai-mcp` — the binary beside a fresh extension is the one that was published last, not the one that was just built | Check the packaged binary's version against the release you just made, from inside the installed extension folder rather than from the repository | manual |

## Why item 1 is first

Because a partial publish is the worst of the three sibling failures
[`development-workflow.md`](.claude/rules/shared/common/development-workflow.md) records: every signal
is green **and** the artefact exists, so there is nothing to notice. The other two — an artefact never
rebuilt, an artefact never deployed — at least leave something behind that looks wrong.

## Running it

```bash
gh auth status                                   # item 1 reads the release through gh
export MCP_VERSION=0.12.1                        # the binary's own tag: mcp-v<version>
node .claude/rules/shared/tools/post-deploy-check.mjs --target 0.26.1
```

`TARGET` here is a **version**, not a URL: what is being checked is what a user receives, and both
places a user receives it from are addressed by name rather than by host.

**Two numbers, on purpose.** The tags are `extension-v<x>` and `mcp-v<y>` and they move
independently — a release of one is not a release of the other. A checklist that assumed one version
would check the wrong artefact half the time, which is how this item was written wrong the first time
and caught by running it: `gh release view v0.26.1` answered *release not found*.
