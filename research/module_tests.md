# module_tests — the harness, the flows it drives, and what it does not prove

> The record the family rule `common/scenario-tests.md` requires of every repository, at the fixed
> name it requires, so a reader looking for the tests of an unfamiliar repository has one place to
> look. Enforced in part by `ScenarioCoverageTests` (`src_mcp/tests/ScenarioCoverageTests.cs`), which
> derives the flow list from the tool registry rather than from this document.
>
> [testing.md](../.agents/conventions/common/testing.md) governs how a test is written and believed;
> this describes what exists.

## Where the harness is

Shared-rule adoption adds real filesystem scenarios: `RuleFilesTests` exercises neutral
PROJECT/local/shared discovery, ordering and missing mount bodies while retaining legacy cases.
`snippetDiscovery.test.ts` calls the same reader the panel uses, against temporary files:
a neutral shared rule is current, and older root/project/local copies take priority.
`src/test/prepareGate.test.mjs` checks frontmatter boundaries and a real Git submodule's
clean/dirty/wrong-pin transitions. A failed build preparation leaves no old generated policy.
These run through `npm test`; no paid model calls occur in that suite. The independent
version/hash test remains, and compares generated delivery with the canonical rule body.

Historical local run on 2026-09-10 at `906ab9c`: neutral C# scenarios first failed with zero discovered files
and no missing mount; all 22 RuleFiles cases then passed. Panel discovery first returned
absent and passed after the location update. Extension suite: 1313 tests, 1312 passed and
one skipped, plus all three build-preparation scenarios. These are local adoption results;
native Codex acceptance and consumer publication remain open in the adoption plan.

Three suites, all in the repository, all in the language of the half they drive.

| Suite | Path | What it drives |
|---|---|---|
| Server scenarios | `src_mcp/tests` | The MCP server: real process, real stdio, real JSON-RPC (`McpContractTests`), and whole rounds end to end (`EndToEndTests`, `LiveRoundTests`) |
| Extension suite | `src_vs_code/src/test` | The extension's pure halves, and the page as it SHIPS — `bundledPage.test.ts` bundles and minifies the real module and runs the page script against a stub DOM |
| Campaign harness | `src_bench` (`coai-bench`) | The whole product from outside: arms of vendors × cases × repeats, driving the real server over the real transport, with a judgement pass over the findings it produced |

The campaign harness is in this repository rather than a repository of its own because it measures
**this** product and nothing else; the rule's separate-repository shape (`dew_flow_benchmark`) is for
a harness that must measure several things. It is C#, like the server it drives, so a renamed field
is a compile error rather than a scenario that quietly stops matching.

## How it is run

Exactly what CI runs — [`.github/workflows/ci.yml`](../.github/workflows/ci.yml):

```bash
# server + core, as an executable (xUnit v3 on Microsoft Testing Platform — never `dotnet test`)
dotnet build src_mcp/tests/CoaiMcp.Tests.csproj
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe

# extension
npm --prefix src_vs_code test

# the campaign harness: build, then one campaign, then the judgement over what it produced
dotnet build src_bench/CoaiBench/CoaiBench.csproj -c Release
./src_bench/CoaiBench/bin/Release/net10.0/coai-bench.exe run  --out artifacts/bench/<name> …
./src_bench/CoaiBench/bin/Release/net10.0/coai-bench.exe judge --runs artifacts/bench/<name>/runs.json …
```

## When it runs

- **The two test suites: in CI, on every push and pull request**, plus the family checks (pin,
  plan lifecycle) and an AOT publish. A release is not cut without them.
- **The campaign harness: by hand, and not in CI** — it spends real vendor subscriptions and needs the
  local GPU for the `local` arm. Run by the operator before a release that changes reviewing
  behaviour, and whenever a question about vendors needs an answer with a number. Its results are
  committed under `research/data/<date>/` and written up as `research/RESULTS_*.md`, which is what
  makes a campaign a fact rather than a memory.

## The flow catalogue

Derived from the tool registry (`src_mcp/src/Tools.cs`) by `ScenarioCoverageTests`. Adding a tool
fails that test until the tool is covered here or declared uncovered with a reason.

| Flow | Covered | By |
|---|---|---|
| `providers` | yes | `McpContractTests` — over real stdio against a real `coai-mcp` process |
| `open` | yes | `EndToEndTests` — the whole story, a flawed plan through to a verdict |
| `review_plan` | yes | `EndToEndTests`, `LiveRoundTests` |
| `review_code` | yes | `EndToEndTests`, `StageGateTests` (refused before a plan reaches `proceed`) |
| `review_document` | yes | `ADocumentIsReviewedEndToEndTests` — a document role nobody compiled in, with every shipped role switched off, reading a real document through the public tool: the round reports one reviewer, the finding carries that role and a DOCUMENT category (`completeness`, which the parser dropped as unknown before this plan), the reviewer's prose comes back in `notes` under its provider and role, the branch's own session is untouched, the snapshot is on disk under its content hash, and `resolve` reaches the document's session when it is given the document and says so when it is not. Asked for BY the plan round in those words: every other test of this feature can be green while `review_document` returns an empty round, because the wiring is the one thing none of them touches |
| `resolve` | yes | `EndToEndTests`, `RoundAuditTests` — a decision is recorded for every finding |
| `status` | yes | `McpContractTests`, `CallerSessionsTests`; its `document` argument by `ADocumentIsReviewedEndToEndTests` |
| `consult` | yes | `ConsultScenarioTests` — a real git repository with real uncommitted work, the fake CLI standing in for codex: a conversation opened, resumed by the vendor's own thread id, closed by the cap, and every refusal read back as a sentence. It carries the two things no unit test can reach — that the SERVER's own collection is what the consultant is handed (the argv recording asserts the modified tracked file, the untracked file and the problem's position in the prompt), and that a consultant which writes in the tree has its advice withheld while the file is left where it is, driven by `FAKECLI_SIDE_EFFECT`, a stand-in behaviour added for exactly this. Its unit-level siblings: `ConsultantArgvTests` (every flag, the absence of `--ephemeral`, no line break in any value), `ConsultantHandleTests` (a handle read off a TRUNCATED stream, and refused when malformed), `FilesystemInvariantTests` (a real repository, a linked worktree's common hooks, and a rewrite that preserves size and mtime), `WorkingTreeDiffTests`, `ConsultationStoreTests` (the sweep's three outcomes), `ConsultCallCounterTests` (the cap holding with no file to write it in), `ConsultantPromptTests`, `ConsultBudgetAndFenceTests`, `ConsultGateFixesTests` and `DiffSplitterTests` |
| The remote reviewer (a Team server vendor) | yes | `RemoteShimScenarioTests` — the REAL `coai-mcp --ask-remote` binary, in its own process, against a real `HttpListener` on a real socket. It is not an MCP tool, so it is not in the registry this table is derived from; it is here because the wiring between an adapter's argv and a separate process is exactly what in-process tests cannot see, and this repository has shipped that break twice |
| A round that ANSWERED NOTHING | yes | `EndToEndTests.ARoundThatFoundNothing_SaysWhatItSent_AndKeepsWhatItWasTold` — a whole plan round and code round in which every reviewer returns an empty findings array, asserting the two `context for review:` lines, each reviewer's prompt SIZE in the opening line, and the eight raw answers on disk under `empty/`. Not an MCP tool, so it is not in the registry this table is derived from; it is here because the three facts live in three classes (`PanelService` assembles, `RoundAudit` writes, `ReviewerExecutor` keeps) and the 2026-09-08 defect was that nothing joined them |
| A role NOBODY COMPILED IN, reviewing a change | yes | `ACustomRoleReviewsAChangeTests` — a repository, a branch, a plan gate, a code gate and a vendor answering through the fake CLI, with the four shipped code roles switched off so the round is the custom role alone: the round reports one reviewer, the finding carries that role's name, and the prompt the adapter was handed is the text from `<dataDir>/prompts/<id>.md`. Not an MCP tool, so it is not in the registry this table is derived from; it is here because every unit test of this feature would have stayed green with the enum replaced by a string and the round still asking a compiled-in list — the question "does a role a person defined actually review anything" is answered by no class alone. It also carries the plan stage's half — a plan-stage role a person added, asked for by the round rather than by a hardcoded array, read back from the round's own `reviewerStates` because two reviewers reporting the same thing merge into one finding under one role. Its unit-level siblings are `ACustomRoleReachesAReviewerTests` (each resolution path, including the plan stage's dealt lenses and the stale-prompt fallback), `TheRoundSaysWhatItCouldNotAskTests` (the edges of the two guards: a prompt file that exists and says nothing, a shipped role whose prompt somebody edited, a role that fails both guards at once, and one role refused once however many lenses it would have been dealt) and `RoleCompositionTests` (what a row is refused for) |
| A stubbed `fetch`'s response | yes | `responseFixture.ts` — the runtime's own `Response` constructor, not an object asserted into one. `responseFixture.test.ts` guards it: no `.ts` under `src/test` may contain `as Response`, checked against the SOURCE with comments stripped, because the compiled output has already erased the cast and a check reading `out/` passes against any number of offenders |
| The client half of the HTTP contract | yes | `teamServerApi.test.ts` — `ask()` against a stubbed `fetch` that carries real `Headers`: the number is read on the success arm and on a 426, an absent header is `0`, an unreadable one (`''`, `' '`, `0x10`, `1e2`, `v2`) is `undefined`, a body that throws still keeps what the server said, and no response at all records nothing. `teamServerView.test.ts` covers the sentence it produces. Not an MCP tool, so it is not in the registry this table is derived from; it is here because the two halves of the contract live in two languages and the server's half has been correct and unread since it shipped |
| The CONSULTANT's settings and its log, across the seam | yes | `npm run test:seam` (`scripts/run-seam.mjs`), legs two and three. **Two.** The extension's own `serverSettingsJson` writes a consultant map naming a vendor nobody configured, and the REAL binary — one live process, driven over stdio — is asked to consult: the refusal NAMES that vendor, which it can only do if the map crossed. Then `COAI_CONSULT_ENABLED=false` is written under the SAME process and the next call refuses by name, which is what proves the live reload as well as the key, and `tools/list` still carries `consult` because a caller that cannot see a tool cannot be told why it is not there. Watched failing with the writer's consultant key neutralised: it answers about `codex`, the shipped default. **Three.** The binary runs a consultation that ANSWERS, against the stand-in CLI the server's own tests use (no model, no money), and a SECOND process answers `--log` for the extension's own `parseLog` — the consultation comes back with its advice verbatim. Watched failing with the projection neutralised: *"the consultation never reached the log. It holds: []"*. **Four** (story B4, 2026-09-15). The DEFINITION crosses: a consultant defined under an id with NO reviewer row — its own runtime and CLI path in the entry — is asked to consult, and it ANSWERS through the stand-in CLI the entry itself names; the advice text is this leg's own, so the third leg's answer cannot satisfy it. Watched failing against the released `mcp-v0.22.0` binary (`COAI_MCP_DLL` pointed at a throwaway build of the tag — legs one to three pass on it, leg four does not): *"the definition did not cross the seam — the server could not consult through a consultant with no reviewer row. It answered: {"error":"the consultant for a 'other' caller is the vendor 'a-consultant-with-no-reviewer-row', which is not configured — …"}"*, which is the same sentence the B4 measurement recorded for that server. What it does NOT prove: that a real vendor CLI answers (that is `ConsultScenarioTests`' fake-CLI scenario and the live runs recorded in the plan). About a genuinely OLDER binary it says one thing now — leg four fails on 0.22.0 as above — and the rest of that skew is measured rather than tested: `scripts/measure-consultant-skew.mjs` drives an older build with the product's own bytes and records the replies (`module_server.md`, *The wire carries the definition*), and the log-table direction is pinned by `a server too old to have the table answers a log with no consultations` and `ADatabaseWithoutTheTable_ReadsAsNoConsultations` |
| The `consult` PROMPT (`/mcp__coai__consult`) | yes | `McpContractTests` — a real process over real stdio: `prompts/list` names it, `prompts/get` returns the instruction with the person's words in it, the argument is accepted BOTH omitted and empty (the plan round caught a required parameter making the ordinary use a protocol error), and the INITIALIZE response carries the instructions that are the fallback for a client listing no prompts. Its unit-level sibling is `ConsultPromptTextTests` — the words travel unrewritten including their whitespace, nothing at all asks the assistant to state the problem itself, and a build log is cut at the server's own 16 KB bound with a sentence saying so. Not a tool, so it is not in the registry this table is derived from; it is here because a prompt collection is registered in `ServeAsync` and nothing in-process would notice it being added to an options object the transport never sees |
| `ask_human` | **no** | It BLOCKS until a person answers in the panel or on their phone, and the wait is the behaviour; a scenario that answered it from a fake surface would be exercising the fake. Its pieces are covered by `EscalationsTests` and `HumanDecisionTests`. |

A note on why the remote row exists at all. The catalogue is derived from the TOOL registry, and a
vendor runtime is not a tool — so nothing would have failed had the remote flow gone uncovered. What
makes it worth a row is the shape of its failures: the adapter builds a command line, a different
process reads it, and the two agree only until somebody edits one of them. The same shape produced
this repository's two most expensive misses — a released server binary that could not serve a single
request under `PublishAot`, and a page whose minified binding was renamed — and in both cases every
unit test was green. `RemoteShimScenarioTests` runs the real binary end to end, including the two
cancellation paths: the shim reaching its own deadline (it cancels and says so), and the shim being
KILLED (it leaves a claim the parent acts on).

**The socket the stub sits on is part of that harness, and it is the reason a correct release did not
ship.** The fixture asked the OS for a free port and then let go of it, which is a race nothing owns:
on 2026-09-15 the `linux-x64` leg lost it, the fixture threw out of `InitializeAsync`, xUnit blamed
whichever test was first, and `mcp-v0.25.0` stayed a DRAFT carrying ten assets instead of twelve.
`LoopbackStub` takes the next candidate instead, bounded at ten, and `AStubSurvivesALostPortTests`
covers: a lost port is retried (the one RED test — verified by setting `Attempts` to 1, the shipped
behaviour, and watching it fail on the real symptom); the bound holds; every code the classifier
claims is honoured, the cases DERIVED from the classifier so the two cannot drift; a failure that is
NOT a taken port arrives as itself, unretried; an exhausted run keeps the platform's own exception;
and — the only one that can fail on a platform nobody has measured —
`TheCodeThisPlatformActuallyReports_IsOneTheStubRetries`, which provokes a REAL collision both ways
on whatever leg it runs and asserts the classifier knows the code that came back.

What this does NOT prove: the collision in every other case is CONSTRUCTED, by holding the port or by
handing the stub a binder that throws. Nothing here reproduces the ambient contention of a loaded
runner, and nothing here can — so the retry is proved and the frequency it was written for is not.
The error codes were measured on two of the six release platforms, each with the port held both ways
(Windows 32 by a socket and 183 by a listener; Linux 98 and 400) — 98 being the one that failed CI.
macOS's 48 and Winsock's 10048 are carried without a measurement, licensed by the Linux result: the
managed listener handed back the raw errno, so a platform on that path reports its own. A leg whose
code is outside the set does not degrade quietly; it reddens the test above and names the code.

### The extension's flows

The VS Code commands in `src_vs_code/package.json` (`contributes.commands`) are the extension's
flow list. They are **not** driven by a scenario that launches an extension host: what is covered is
each command's decision — the pure function it routes to — plus the page as it ships. The gap is
real and named here rather than implied:

| Flow | Covered | By |
|---|---|---|
| The chat tab's page | yes, as it ships | `bundledPage.test.ts` runs the bundled, minified page and drives its **Send** button through to the one posted turn, asserting the composer locks on the way — the flow the pinned composer added. `chatPage.test.ts` runs the same script from source through `runChatPage()`, which answers only the ids the page actually renders, so a control that stops being rendered stops being testable rather than silently passing |
| A limit that waiting cannot clear | yes, over a real process | `GateReportingTests.cs` holds the vocabulary: the measured codex answer is hopeless, two NEGATIVE cases are not (a throttle carrying a sales footer, a throttle naming how long to wait), and both halves of the line choice are pinned — a terminal line wins over an earlier marked one, and an ordinary throttle is still reported when no line is terminal. **The guard that matters most is `EveryHopelessPhrase_HasAnObservedAnswerThatSurvivesTheLineFilter`**: `Reason` reads MARKED lines only, which is safe solely because every hopeless phrase happens to be carried by a line `Phrases` also matches — a property of the strings, not of the lists, and nothing enforced it until this table did. Add a phrase with no vendor answer behind it, or one whose answer is not marked, and it fails. `BoundedSchedulerTests.cs` drives the real scheduler against a real process: one launch, counted in a file, AND no progress note announcing a wait, because one launch with a long wait is still what was complained about. **NOT covered**: no stated-wait parsing — *retry after 20 seconds* against *retry after 3 hours* — because no sample of one has been observed from a vendor this product runs; and `ScenarioCoverageTests` is keyed by TOOL, so this flow adds no row there and the catalogue does not move. |
| The rounds log's pager and its two views | yes, the page is run | `roundsLogPaging.test.ts` — the pager's disabled state is asserted against the PARSED stylesheet and a modelled element, not its text: a rule keyed on something no button carries satisfies every substring search and paints nothing. The hover half is the one that matters, because a browser matches `:hover` on a disabled element, so the test asks whether any hover rule still REACHES a dead control and names the ones that do. The two views are asserted across a PAGE BOUNDARY every time — the order of the view filter against the slice is invisible on one page — and a search matching both kinds is asked in each view. `roundsLogPage.test.ts` keeps the inverted guard that allows exactly the known button selectors and reddens on a new one. **Every rule and filter was proved by deleting it**, which is how the *back to page one* test was found passing with `firstPage()` removed: `render`'s clamp rescued it while the other view held one page. NOT covered: no layout engine, so the declarations and the cascade are asserted and the rendering is not |
| A copy control that says it landed | decisions and the rendered page | `chatPage.test.ts` — the tick beside a *Copy block* / *Copy answer* control. Four things pinned TO EACH OTHER: the acknowledgement is delivered and the mark lands on that control and no other; a press ALONE marks nothing, which is the whole point, since a control that confirmed on the press would confirm just as confidently while the clipboard was held by something else; the mark survives a state push rebuilding `#messages` under it; and two presses half a second apart leave the second one's second intact rather than the first timer cutting it short. The CSS half is parsed with `cssRules.ts` and the selector is matched against the control the page actually renders — a source-text assertion passes on `.never[data-copied="1"]`, which matches nothing. The host's own half is `acknowledgement` in `answerCopy.ts`, which is a function precisely so a test can reach it: nothing in `chatCommand.ts` can be imported here. **Every guard was proved by breaking it** — the rule removed, the condition inverted, and the harness reverted to answering `[]`. NOT covered: no layout engine, so the declarations and the cascade are asserted and the rendering is not |
| A failed turn's **Try again** | yes, as it ships | `bundledPage.test.ts` renders the shipped page with a failure the host says is retryable, asserts the control is in the MARKUP a person is handed — the markup alone, because the page's own script carries the `[data-retry]` selector as a string and a whole-document match would be true whatever the state is — and presses it through the delegated region listener to the one posted `retry`, which names the transcript length it was drawn for. A companion case asserts no control at all when the host says there is nothing behind the failure. What it does NOT prove: a second press is refused by a flag written on the pressed element, and this stub fabricates a fresh element per click, so that guard lives in `chatPage.test.ts` where the same element can be handed over twice. The host half is `chatPresets.test.ts` (`retryFrom` reads the trailing message and nothing else), `chatMessages.test.ts` (a retry naming no whole number is ignored, so there is no wildcard press) and a structural guard in `chatWiring.test.ts` (a retry does not recompute the carry the failure preserved for it) |
| Folding a long question | yes, as it ships | `bundledPage.test.ts` renders the shipped page with a question over the character bound, asserts it came back folded and carrying a key, and presses the control through the delegated region listener — then reads the rule out of the page's own `#folds` stylesheet, which is where the open set lives. `chatPage.test.ts` holds the rest: the boundary of `isLong` on both arms, that `foldKey` is stable and content-derived rather than positional, that the control names how much is hidden in the unit the question actually has, that an answer is never folded, that an opened question survives the transcript being replaced AND closes again on a second press, and that a key which is not the host's own base-36 hash is never written into a stylesheet |
| The rounds log page | yes, as it ships | `bundledPage.test.ts` bundles and minifies the real module and runs the page script — the only test that catches a minifier-renamed binding, which shipped twice |
| Exporting a round, and a selection of them, to CSV | decisions, and the cross-process SEAM | `roundsCsv.test.ts`, `roundsExport.test.ts`, `roundsDbMany.test.ts` — the column list; that an absent measurement stays an EMPTY cell and never a zero, because zero is a number somebody measured; the formula-injection guard including a payload hiding behind a leading space, which a naive guard passes and a spreadsheet does not; the instant written twice, stored-UTC and local-with-offset; one line per finding carrying the round's own columns and the decision in the page's own words; and a round that genuinely found nothing still getting its line. On the selection: that filtering never unmakes a selection while a round scrolled out of the loaded window does leave it, the hidden count on the button, and the confirm above five hundred. On the SEAM: that `--findings-many` is asked with a keys file and every answer is paired to its round **by the key the server echoes back, never by position**; that exit **64 and only 64** means an older server and falls back to `readFindings` four at a time; that 65/69/74 fail the rounds rather than being retried a different way; that a cancel reaches the child process rather than only stopping the wait; and that a round whose findings could not be read is written `not recorded` or `failed` rather than as a round that found nothing. NOT covered: the save dialog itself, which is `vscode` state |
| Phrases — the list, the tab, and the copy | decisions only | `phrases.test.ts`, `phrasesEdit.test.ts`, `phrasesPage.test.ts`, `phrasesSection.test.ts`, `phraseCopy.test.ts`, `panelPhrasesScript.test.ts` — that a mistyped row is dropped on its own rather than taking every other phrase with it and that nothing in the settings file makes the extension throw; that a row with words and no name keeps its words and is named from its first line that HAS words, cut to sixty characters with an ellipsis that says so while the phrase itself is never shortened; that the phrase is kept byte-exact, indentation and trailing newline included; that *Copied* is posted only after the clipboard accepted the text, and that two presses land in press order rather than racing. `panelPhrasesScript.test.ts` runs the generated page script, which is what caught an unterminated regex the source-matching tests passed straight over |
| Install / update the server | decisions only | `install.test.ts` (RID choice, asset names, companion policy, the release workflow's own guarantees) |
| Copy the config block / the CLAUDE.md snippet | decisions only | `install.test.ts`, `panelServerPromptAgreement.test.ts` |
| Panel settings writes | decisions only | `settingWrite.test.ts` — the routing decision, not the `vscode` call it leads to |
| Add / sign in to / sign out of / remove a Team server | decisions only | `teamServers.test.ts`, `teamServerAuth.test.ts`, `teamServerApi.test.ts`, `teamServerView.test.ts` — the canonical URL and token path (against the shared cross-language fixture), the scope trust boundary, the sign-in compensation, the renewal flag, every request's failure path with a stubbed `fetch`, and the section's markup. The `vscode` calls those decisions lead to are NOT covered, for the reason in the row below |
| A reviewer's status mark in Active rounds | the decision, by calling it | `activeRounds.test.ts` — `statusMark` is EXPORTED and tested by calling it, one case per status, asserting the exact class AND the exact glyph, because a substring assertion over the generated page cannot tell a branch that returned the wrong glyph from one that returned the right one. The set of four is additionally asserted distinct, which the per-status loop cannot see on its own. An unrecognised status gets a span carrying `class="mark"` with no modifier and no glyph — both halves asserted, so neither "invented a meaning" nor "dropped the column" can pass. `panelView.test.ts` pins the whole line as it is assembled. The CSS is asserted as a MAPPING — done/green, running/blue, queued/yellow, failed/red, each naming its own `--vscode-charts-` variable — because "names some charts variable" is green when done is red. NOT covered: that the glyph renders in the sidebar's font on a machine this has not run on; every glyph used was already shipping elsewhere in this product, which is the argument standing in for that check |
| The Consultant section's frames and colours | the decision by calling it, plus ONE integration assertion | `consultant.test.ts` — `callerColour` is exported and tested by calling it, per caller id, **against the allocator rather than a hard-coded hex** so the test cannot drift from the palette; `other` is asserted to be the neutral token AND not what the palette would give it, because asserting only the first would pass if the palette happened to return that token. `panelView.test.ts` carries the one that actually guards the feature: the rendered page must show the SAME colour for `codex` on its reviewer card and on its caller row. Verified by deletion — removing the palette from the `consultantBody` call reddened **only** that test while every `callerColour` test stayed green, which is what proved the unit tests alone were insufficient. A companion test pins that the frame was not bought by rewriting the controls: every `data-setting`, `data-caller` and `id` hook survives, and the section's hints are compared whole against an unframed render. NOT covered: that the frame is visible. Nothing here has a layout engine — see *What this does NOT prove* — so the declaration and the inline colour are what is checkable |
| A phrase's colour, in both places at once | the decision by calling it, plus a CROSS-SURFACE assertion | `phrases.test.ts` — `phraseColours` is tested by calling it: three ids get three colours AND a second allocator over the same list answers the same, because "all different" alone passes for an allocator that is unstable between builds. Two properties are pinned that the plan round predicted the opposite of: **twelve phrases still get twelve colours** (the anchored vendor slots are offered last, not withheld, so the first repeat is the thirteenth) and that **a NON-COLLIDING addition moves nobody** (a name's slot comes from a hash of the name itself). The collision case is pinned too, and it is real: adding `phrase-11` to two phrases moves `phrase-2`. So the allocator is not subset-stable, the cross-surface promise rests on both surfaces reading one saved list, and the test says so at the smallest list that shows it. `phrasesSection.test.ts` carries the one that guards the feature: the same phrase's colour is the same STRING in the editor page and in the panel — and a companion renders the two surfaces **one phrase apart**, because they are separate webviews on their own clocks and a shared phrase must not change colour because of a phrase it is not. `phrasesPage.test.ts` asserts every row in an eight-phrase list wears a distinct colour (not merely "a colour", which a page painting all eight the fallback would satisfy), that `.phrase` still carries its border and 3px edge — without which the inline hue paints nothing — and that each label's `for` names a control `id` that exists, which is what makes it a label rather than a caption. NOT covered: that any of it is visible. No layout engine — see *What this does NOT prove* |
| Clearing a chat row from the spending chart | decisions, the page's own CLICK, and the wiring structurally | `chatSpendRows.test.ts` — `rememberedChat` by calling it: a record AT the mark is forgotten and a later one kept (the `>` the reviewers' `remembered()` uses); forgetting one model leaves the vendor's other models and the same model under another vendor alone, which is what fails if the key collapses to the provider; and **what a line wrote down decides its row, not the preset list as it is today**, which is what fails if the filter resolves through `vendorOf` alone. Both key rules were proved by reverting: collapsing the key reddened the second, and dropping the written-down vendor reddened the third **and a pre-existing chart test**, which is how the extraction was shown not to have changed the chart. `forgetAChatRow.test.ts` **runs the page**: it captures the listener the rounds-log page registers on `document` and calls it with an event whose target answers `closest`, asserting the press posts the vendor AND the model — this caught a real defect, the page's handler posting only `id` while the markup carried `data-model`, and reverting that field reddens exactly this test. The same file holds the markup pair (chat control present, reviewers' control still present), the unnamed row having no control, a model id containing a quote being escaped rather than forging an attribute, and — as a source assertion, because `panelProvider.ts` needs a host this suite has none of — that BOTH ledgers pass through the filter and that a machine which has never forgotten anything reads an empty map. NOT covered: the modal itself, and that a mark survives an extension restart |
| Add a reviewer from the CATALOGUE | decisions, plus the two API flags structurally | `vendors.test.ts` — that the entry a person searching for *Claude Code* filters on carries those words **selected by `id === 'claude'`**, because an assertion that some label contains them stays green while that entry keeps its old name; that `presetsOffered` returns the catalogue WHOLE whatever is already configured, giving a taken id the next free `<id>-2` and marking it a second row, with the un-taken case asserted in the same test so it cannot pass against a function that marks everything; that a blank base stays blank, which is what keeps the custom-endpoint flow asking for a name; and that `reviewerPickItems` puts the new id in the `description` and leaves it empty otherwise. `addAReviewerFilters.test.ts` covers what no pure test can reach — `matchOnDetail` **and** `matchOnDescription` on the pick's options object, matched as a whole object rather than as two loose substrings, and the old `VENDOR_PRESETS.filter(` asserted GONE rather than the new call asserted present. NOT covered: the quick pick itself. Nobody has driven the real list, so that a person typing into VS Code's filter box sees the entry is inferred from the flags, not observed — an extension host is the standing gap named at the end of this file |
| Add a reviewer FROM a Team server | decisions only | `remoteVendorRow.test.ts`, `remoteVendorCard.test.ts` — the row shape, the catalog-fed model list, the canonical server match, and the fields a remote row must not show. The quick-pick itself needs an extension host |
| A reviewer the server cannot run, badged on its card | decisions only | `providersBadge.test.ts` — the three states and that only `unavailable` draws anything, the parser against every shape a different build could hand it, and a row the server did not mention reading as UNKNOWN rather than as fine. The spawn of `--providers` itself is the row below |
| Team-server spending, and *Company* | decisions only | `teamUsage.test.ts` — the per-server block, the admin gate, and what a server that has not answered renders as |
| Mirroring the settings to the file the server reads | decisions, the CONTENTION, and the SEAM | `settingsSync.test.ts`, `settingsLock.test.ts`. Covered: that a change reaches the file with no panel open; that an older build stands down from a newer stamp and says so once per version; that a file which cannot be READ is not a file that is not there; that the read, the comparison and the write happen in that order inside one section; that two syncs over one lock and one file interleave to exactly one writer and one whole payload; and when a lock left by a dead window may be broken, including a clock that went backwards. NOT covered: the lock itself — `fs.open(…, 'wx')`, the owner token, the stale break and the atomic rename all need a real filesystem and two real extension hosts, which is the row below. **The seam IS covered live**: `npm run test:seam` (`scripts/run-seam.mjs`) writes the file with the extension's own `serverSettingsJson`, signs a throwaway data directory in at the token path both sides derive independently, serves a catalog from a loopback HTTP server, and runs the REAL `coai-mcp --providers` against it — asserting the Team-server row resolves under the name its SERVER knows. Watched failing with `remoteVendor` removed from `vendorsEnv`, which is the defect of 2026-09-07 reproduced by the two implementations rather than described by a fixture |
| The Consultant section — how the SERVER resolves what was stored | yes | `ConsultScenarioTests` (the five B3 scenarios, against the real service and the stand-in CLI) with `ConsultantResolverTests` beneath it. Covered: that a consultant DEFINED with no reviewer row at all still resolves, and `settings.Providers` is never read for it; that a definition on a runtime no consultant may run on — `remote` above all — is refused BY NAME **before any `ProviderSettings` exists**, which is the invariant that a working tree is never routed at a Team server; that a LEGACY reference still works with no rewrite, through a row matched case-insensitively and borrowed from **enabled or disabled** (the "switched off" refusal is gone, and that reviewer flag is a fact about reviews), else through an id that is itself a consulting runtime, else refused by name; that rule (a) is asked BEFORE rule (b), proved on `codex`, the id that is both; that a runtime is recognised without case on BOTH halves and travels in the allowlist's spelling; and three guarantees about a RESUMED consultation — it keeps the vendor, model and runtime frozen on its record, it is refused when its own frozen runtime is one no consultant may run on (whatever today's settings say), and the definition that lends it an endpoint is the one under its OWN caller kind rather than whichever the map iterates first. Each of those three was watched failing with its guard removed; the caller-kind test uses a SORTED map on purpose, because with a plain dictionary the absence of the guard shows up only some of the time. **NOT covered:** a real vendor CLI answering — that is the fake-CLI scenario and the live runs in the plan. The definition CROSSING the seam was this row's other gap until story B4 (2026-09-15): it is now the seam's leg four (the row above), with its measurement against the released server half in `scripts/measure-consultant-skew.mjs` and `module_server.md` |
| The Consultant section — what an edit STORES | decisions only | `consultant.test.ts`, `panelServerDefaultsAgreement.test.ts`. Covered: that a legacy `{vendor, model}` entry resolves into a definition on READ and writes nothing (rule (a) a reviewer row enabled or disabled, (b) an id that is itself a consulting runtime, (c) unavailable, preserved raw with its reason); that the first EDIT stores that definition, so the consultant stops following the reviewer row; that a vendor change clears the model and lands the one the new vendor will really use, while re-sending the vendor already chosen changes nothing; that a blank id, an unknown setting key and an unknown caller kind each write nothing; that an unplaceable entry gains no invented runtime and refuses an endpoint; that a field or a caller kind this build cannot name survives an edit beside it; and that the shipped four pairs agree across all THREE copies — the C# `ConsultantRouting.Shipped`, `DEFAULT_CONSULT`, and the manifest's `coai.consultants.default`, which nothing was reading until 2026-09-15. **NOT covered: that `panelProvider` hands the write the SIDE-AWARE reviewer rows.** The unit tests prove the function materialises from the rows it is HANDED — two different row sets give two different results — and cannot see which rows the one line above them read; observing that needs a running extension host, as it does for the chat rows below. The controls for base URL and CLI path do not exist yet (story C5 of [PLAN_the_consultant_has_its_own_vendors.md](PLAN_the_consultant_has_its_own_vendors.md)); the write path accepts their keys already |
| Chat with other AI — the trigger | decisions only | `chatSettings.test.ts`, `chatCapture.test.ts`, `chatModels.test.ts`, `chatWiring.test.ts` — every settings fallback; both doors and all three `chatAutoSend` values; the clipboard borrow including the case that must NOT restore, the unreadable clipboard that must not be written to, and the marker collision; which models may be asked and which are refused by name; and the manifest wiring itself — that the command, the keybinding and the `webview/context` item all name the id `extension.ts` registers. NOT covered: the synthetic `Ctrl+C`, which needs a real webview with a real selection — it was MEASURED with a probe extension instead (638 characters, 1725 ms) and the numbers are in `module_extension.md` |
| Chat with other AI — the conversation | decisions only | `cliChatSession.test.ts`, `chatPage.test.ts`, `chatPanel.test.ts`, `chatMessages.test.ts`, `chatPrompt.test.ts` — the four ways a long-lived child ends and what each says; the page as it renders; the host boundary that refuses a model the conversation was not offered; and the two turns a model is ever sent: the opening one with its fenced passage, and the CARRIED one that hands a whole conversation to a model that never heard it (questions and answers both, attributed, fenced with a per-turn id, bounded at 60 000 characters with the loss stated). NOT covered: the switch itself — replacing a live session under an open panel is `vscode` state, and it belongs to the row below |
| Chat with other AI — what a turn COST | decisions, and the file | `chatUsage.test.ts`, `chatUsageFile.test.ts`, `chatRows.test.ts` — that a cumulative vendor's numbers are differenced against what it last said and that the rule is keyed on the RUNTIME rather than on a vendor row a person can rename; that money is differenced by the same rule so the two cannot drift apart; that a turn which was stopped or failed is still written down; that a turn nobody reported numbers for reads as UNKNOWN and never as free; and, against a real temp directory, that the ledger appends rather than truncates, that queued writes land in the order they were asked, that a half-written tail costs itself alone, and that a ledger which cannot be written costs the record and never the person's answer. On the log side: the Kind column, the Kind facet, the merge of the two ledgers by time, and that a chat row leaves the columns it has no answer for empty. NOT covered here: several PROCESSES appending at once, which a unit test cannot fork and mean anything by — measured instead, see the row in *What this does NOT prove* |
| Chat with other AI — WHAT IT COST ON THE SPENDING PAGE, and HOW OFTEN IT WAS REACHED FOR | decisions, the wiring, and the page | `chatDoors.test.ts`, `chatDoorsFile.test.ts`, `chatSpendRows.test.ts`, `chatSpendSection.test.ts`, `chatWiring.test.ts`, `chatPresetsPage.test.ts`. Two counts that no turn ledger can answer — *Asked* (`take the question` + `add the question`) and *Opened* (every door) — because a turn is written when a turn FINISHES and `add the question` finishes none. Covered: the door record round trips and a torn line costs itself alone; **that a door line would be read as a TURN by the other ledger’s parser**, which is why it lives in a file of its own and is asserted so that moving it back breaks a test rather than a page; that a door this build has never heard of still counts as an opening but not as a question; the arithmetic — per vendor AND model, the window bounding tokens and both counts but never the all-time column, a bill and an estimate never summed into one number, a row that is part billed and part not keeping BOTH, a cost nobody could have charged treated as no cost, a record whose instant cannot be read belonging to no window, a door that resolved nothing landing in its own named row, and the section total counted from the ledger rather than from the rows that survived the filter; that the whole ledger is read ONCE rather than once per row, counted rather than timed; and the rendered tab — both headings, the rule between them, a total each, and the counts in the row. **The command wiring is asserted against the SOURCE of `extension.ts`, with the door list DERIVED from the manifest’s menus and keybindings**: each door records exactly one invocation, before the work that can refuse it, and nothing records a door the manifest does not offer — so a sixth way into a chat fails this rather than quietly counting zero. Against a REAL directory, like the turn ledger beside it: that the line reaches the file and comes back, that a second invocation is appended rather than written over the first, that queued writes land in the order they were asked for, that a directory nobody has written to yet is made rather than refused, that a half-written tail costs itself alone, and that a ledger nobody has written is no invocations rather than a failure. **And that the vendor is the one WRITTEN DOWN**: a preset is a row somebody edits, so resolving an old line through the list as it stands today would move a year of history to another vendor - the recorded value wins, and the resolver answers only for lines written before the field existed. NOT covered: the commands through a real extension host, which is the row below |
| Chat with other AI — WHERE A HANDOVER STARTS | decisions, the wiring, and the page as it ships | `chatCarry.test.ts`, `chatWiring.test.ts`, `chatPage.test.ts`, `bundledPage.test.ts`. A conversation is handed whole to a model that never heard it in five places — a Team server every turn, a model switch, a re-ask, a vendor that lost the thread, and the first turn after a reload — and **Carry nothing above** moves where that handover begins. Covered: the position itself (past the end carries nothing; a negative one never reaches `slice`, where it would be an offset from the END and carry the last message instead of the suffix; nothing but a finite non-negative integer is a position at all, from the page and from the store); that ALL FIVE consumers go through the one function, counted structurally rather than listed, so a sixth added later without the mark fails; that a mark set AFTER a switch reaches the conversation that switch had already staged, which is the case the feature exists for; that the mark only moves forward, since the button is offered on the last answer alone and a lower position is a stale page or a forged message; that a re-ask brings the mark back with the transcript it truncated; and, in the SHIPPED bundle, that the button posts the index it names through the delegated listener — with a `closest` stub that honours the real selector, because one that did not made this very test pass against a button that could not have been pressed. NOT covered: an actual Team-server turn or model switch end to end, which needs the extension host in the row below; what is asserted instead is that both build their carry through the one function |
| Chat with other AI — FINDING A CONVERSATION AGAIN, and forgetting one | decisions, the wiring, and the rows | `conversationPicker.test.ts`, `conversationChoice.test.ts`, `conversationPickerWiring.test.ts`, `chatStoreCache.test.ts`, `chatStoreSweep.test.ts`, `chatStoreFile.test.ts`. **CoAI: switch conversations…** lists what is open here, what another window holds, and what is only on disk. Covered: the rows and their order, with Open first and an open conversation never repeated among the closed ones; that only a conversation carries an id, so a notice or the cut line is not something an accept could open; that a conversation another window announces is drawn as `elsewhere` and is neither reopened nor forgotten, while this window’s OWN heartbeat is never read as somebody else’s and a window that has gone quiet stops holding its conversations; that what is TYPED is matched BEFORE the hundred-row cut, without which the hundred-and-first conversation could not be found by typing its own title; the folder label shown only when folders differ, widened until two projects of one name are told apart; every sentence a failed choice produces, with `gone` taking the row away and `unreadable` keeping it; and, against a REAL directory, that forgetting removes the index entry and RENAMES the transcript into the quarantine under a dated name the sweep already retires — and says so distinctly when there was no transcript to archive. **The widget itself is asserted against the SOURCE of `conversationPickerCommand.ts`**: that it uses `createQuickPick` and never `showQuickPick`, that the list is rebuilt rather than reopened by a forget, that the picker is not closed until a tab is actually on screen, that one accept and one forget run at a time, and that the manifest’s chord carries a macOS form. NOT covered: the QuickPick itself — item buttons, the title button and `Alt+Delete` inside it are `vscode` state, which is the row below |
| Chat with other AI — WHICH CONVERSATION BELONGS TO THIS TAB | decisions, the store operation, and the wiring | `chatSource.test.ts`, `chatStoreRefile.test.ts`, `chatSourceWiring.test.ts`. A conversation gets a durable identity — a file’s uri or a Claude session’s id — and the workspace root it is filed under, captured when that identity is. Covered: which root a path belongs to (longest wins, because roots nest; segment boundaries, so `coai-old` is not inside `coai`; blind to case and separator, since paths arrive from two programs); what a rename moves, including a renamed FOLDER reported as one entry and the closest of two overlapping moves; a session id read off either platform’s separator and refused for anything that is not one of those files; and, against a REAL directory, that a moved file takes its conversation with it — source and root in ONE revision, nothing else rewritten — that a record whose source has moved on is left alone, and **that the store refuses to WRITE a record it could not read back** (a contradictory origin pair saved happily and was then unopenable for ever). **The wiring is asserted against the SOURCE** of `chatCommand.ts` and `extension.ts`: that a record is written with the conversation’s own source rather than a placeholder, that the pin captures the folder the session was found in and writes explicitly (the page’s dedupe guard would never have carried it), that all three writers share one queue, that the uri comes from the MATCHED tab rather than whatever is focused, that a restored conversation which lost its source pins again, and that `onDidSaveTextDocument` is NOT listened to — the editor reports no previous uri for a saved untitled buffer, so a listener would attach a conversation to the wrong file. NOT covered: the rename event, the tab registry and the panel end to end, which need the extension host in the row below |
| Chat with other AI — WHICH CONVERSATION IS THIS TAB’S | decisions only | `chatGoto.test.ts`, `conversationChoice.test.ts`. The decision *go to* takes, over the whole matrix: tab kind × what this window already holds × how many saved conversations carry this tab’s source × whether its own Claude session is in doubt × what state the index is in. Covered: that what is open is revealed without reading anything, and on the TAB rather than a source; that exactly one match reopens and two are a picker rather than the newer of them; that a conversation filed under another root of the same window is OFFERED rather than silently dropped, and is not bound to this tab; that one root spelled two ways is one root, under the filesystem’s own case rule; that a source of `none` matches nothing however full the store is of sourceless records; that an ambiguous Claude tab is offered its root’s conversations — the picker was empty until four reviewers found it — while a DOCUMENT tab never produces a session picker whatever the flag says; that an index still BUILDING is not an empty store and one that could not be READ never reopens; that the act-time check verifies source AND workspace, so a record re-filed into another project between the decision and the press cannot bind; and every narrowed title, each reason saying something different. NOT covered: the command and its six arms, which are the row below |
| Chat with other AI — GOING TO THE CONVERSATION ABOUT THIS TAB | the wiring, against the source | `chatGotoWiring.test.ts`. The command that carries C2’s decision out — the half `chatGoto.test.ts` cannot see, because a build where every arm revealed the wrong thing, bound a cross-root record or started a duplicate would pass every decision test. Asserted against the MANIFEST and the SOURCE of `chatGotoCommand.ts` and `chatCommand.ts`: that the command is declared, chorded on both platforms and offered in both right-click menus; that it records its door BEFORE the work, like the five doors before it; that each of the six answers calls the thing it names and **a seventh would be a compile error**; that `start` opens the picker with the offer under the cursor and creates NOTHING; and that a conversation already live under another key is REBOUND rather than merely revealed, while the reload serializer binds nothing. **Binding is asserted as a question asked AT THE PRESS** — `bindTo` is a function, handed to every arm that binds (counted, because one arm holding a precomputed handle is the whole defect back), re-checking source AND the record’s OWN workspace, that the tab is still open, and that it still holds nothing; the record itself is re-read from DISK, and the one-press latch is set inside the `try` so a throw cannot wedge the command for the life of the window. Also: that a record that moved warns AND opens the list rather than stopping; that a session walk which FAILED is reported as `unsure` and said on the console rather than being read as “no sessions” — which would offer to start a duplicate — and that the walk runs under a progress notification. **And the three the second round found**: that the record is re-checked against THE TAB’S root through the very function the decision files by — it was being compared against its own workspace, a root against itself, which passed for every record in the store and undid the cross-root rule at the last step; that the offer to start one can actually be CHOSEN, is drawn only where a caller has said what choosing it does, hands over to the ordinary door on the tab the row names, and refuses when that tab is no longer the one in front; and that a conversation already live is rebound from the PICKER’s accept too, not only from the reopen arm, through one shared helper — with the ordering asserted, because the claim that a second panel was built is refuted by the registry being walked by the store id before anything is read back. Every one of those seventeen guards was proved by breaking it and watching the test name the symptom. NOT covered: the command through a real extension host, which is the row below |
| Chat with other AI — TAKING ONE BLOCK of an answer | decisions, the wire, and the page as it ships | `renderAnswer.test.ts`. An answer's only copy control copies the whole of it, so every fenced block and every blockquote the renderer draws now carries a copy row of its own, and one reserved fence tag — ```` ```reply ```` — makes that row read *Copy the reply prompt*. Covered: that the first block is numbered **0** and not 1, which is the `push`-returns-length trap a plan reviewer caught before it was written; that a fence and a quote each get a row, and so does a fence nested inside a list item — the shape a top-level scan of the markdown cannot see, and the one that already differs from what the renderer emits on 1 of 39 real stored answers; that a quote CONTAINING a fence gives two overlapping controls, the inner numbered first because a quote is recorded on the way out, so the numbers run in the order the rows appear; that a block past `MAX_DEPTH` is drawn as text and therefore earns no row and consumes no ordinal, asserted as the **exact** ordinal list rather than as two lists agreeing — both sides come from one implementation, so a length comparison would have stayed green while the guarantee was gone; that a `reply` fence differs from a `text` fence by its class and its label and by nothing else; that a renderer not told which message it is drawing emits **no** controls at all, which is what keeps this story from shipping a button nothing can act on; and that `button` — the twenty-sixth tag the renderer may emit and the only one that is ours rather than a model's — only ever appears in one exact shape, with a companion assertion proving that scan still finds one. Teeth proved by breaking the code twice: returning `push()`'s value fails 5 tests, suppressing the quote's row fails 3. **And the whole way across** (`answerCopy.test.ts`): the markup `chatMessagesHtml` produces, scraped for every control's coordinate as DATA, each one turned into the message the page's listener would post, run through `chatCommandOf`, resolved by the real decision and written to a fake clipboard — asserted against **hand-written** expectations, never against the same array indexed twice, which would pass under any consistent-but-wrong numbering. It stops at a fake clipboard rather than at `answerBlocks` because the Definition of Done promises what reaches the CLIPBOARD, and a test ending at the enumerator would stay green while the hook failed to forward or the write never happened — raised by two vendors independently on the plan round. Covered there too: that a control drawn for an answer since REWRITTEN to a different text of the same block count refuses rather than copying the new one, which is the case no range check can see; that an ordinal past the end refuses; that a rejected clipboard write is said out loud on the block path **and** on the whole-answer path, whose bare `void` had been silent since it shipped; and that two presses land in the order they were made rather than in the order they resolve. Teeth: removing the signature check goes red naming *the stale control copied the new text*, and numbering from `push()` goes red naming the wrong TEXT — and, on the reply block, an empty clipboard, which is the shipped symptom that defect would have had. In `bundledPage.test.ts`, the SHIPPED minified page: a press posts `copyBlock` with the block, the message and the signature as a STRING (coerced to a number it would be `NaN` for every signature that is not all digits, and every press would be refused), and carries no text; red when `[data-block]` is dropped from the selector. In `chatMessages.test.ts`: the coordinate is accepted only whole — index, block and a non-empty signature at or under the 64-character cap, the boundary itself asserted — and in `chatPage.test.ts`: that no two adjacent controls share a label, now that the message-level one reads *Copy answer*. **And the JOIN, added after CodeRabbit read #273**: those two halves were each exercised and never met — one proved the shipped listener forwards a dataset, the other that a coordinate resolves to the right text, and a parser or listener that dropped a field would have passed both. `bundledPage.test.ts` now renders a real answer, takes the REAL attributes of its second control, presses it through the shipped minified listener, and carries the posted message through `chatCommandOf` into the host decision and a fake clipboard. Watched red by making the parser answer block 0 for every message: the reply control then copies the FIRST block, which is exactly the shape neither half could see. **Also from that review**: a corrective write is itself a write, and a press can land while one is in flight — the first version started a correction and forgot it, so it could settle last and restore text already copied past. Red first, naming the clipboard holding `second` where the person last copied `third`. **NOT covered**: a real press in a real editor, which needs the extension host — the same gap every row here has |
| Chat with other AI — NEW CHAT, the clean slate | decisions and the wiring | `chatFresh.test.ts`, `chatFreshWiring.test.ts`. Covered as values: the slate a reset installs, field by field — and, in both directions, every field it must NOT touch, because a reset that clears too little carries the old conversation into the new one while a reset that clears too much throws away the model somebody chose or the tab’s own identity, and both are silent; that the three marks saying what the disk already holds are DELETED rather than emptied, since the push that writes a conversation down reads “never written” from their absence and would otherwise never write the new record at all; that the clock is an argument; and that the two sentences say what happened, the failure naming its reason and saying the conversation still works. **The ORDER is asserted against the SOURCE**, which is where this story could go wrong invisibly: the generation is bumped BEFORE anything is stopped, so a question queued behind the running answer never begins; the ending is stop → wait for the turn chain → drain the disk queue → dispose → release, each position answering a specific way of losing or mis-filing an answer; the old record is archived only once the ending succeeded, and BEFORE the new id is published; the slate is applied as ONE value so a field added to it cannot be forgotten; a reset that FAILED archives nothing and leaves the conversation not merely live but usable; one reset runs at a time, with the latch released in a `finally`; a turn carries the generation it was QUEUED under and refuses to run under another, handing the words back to the composer; and what a turn writes at the END is guarded on the save id instead, because between a reset beginning and the slate being wiped the turn in flight is still writing into the old conversation. Plus the page seam: its own message type, the quotation cleared, the id merged into the held state, and the line recorded in `lastWritten` so it cannot go stale. **And what the code round added**: that archiving is part of the all-or-nothing, a store refusal going through the same recovery an ending failure does rather than leaving the old record open under a page that says it was archived; that the new record is WRITTEN before its id is published, since the page hands that id to the serializer after a reload; that the dead session stub is installed on the SUCCESS path too, because between the reset and the next question a stop or a switch still reaches the session it has just disposed; that the disposal and the release run whatever failed before them, so a session that would not stop is not left unreferenced and still running; that a question typed DURING a reset goes back to the composer instead of being queued into a conversation being torn down; and that the archived sentence travels with the state push, because the region it is written in belongs to that push and a line written straight into it was wiped on the following frame. **And what the final round added**: that EVERY field of a thread is classified — the reset's projection is exhaustive over `Thread` by a compile-time partition, so a per-conversation field added later and forgotten stops the build and names itself rather than silently surviving into the new conversation; that a window with nowhere to keep conversations REFUSES the reset instead of wiping one that has nothing to be archived into, with the nothing-said case checked first because it needs no store; that the new id is published only once the DISK has the record, since the write chain cannot reject and a drained queue is not a saved record; and that the one-reset-at-a-time latch lives on the thread rather than in a module-level set of object identities that outlives what is in it. Every one of those guards was proved by breaking it — thirty in all across the three rounds: twenty-seven went red naming their own symptom and three would not compile, which is its own guard. Three assertions passed their break the first time and were strengthened, each having matched a fragment rather than the whole condition. **And the button that makes it reachable** (`chatPage.test.ts`, `bundledPage.test.ts`, `helpCoverage.test.ts`): that *New chat* is in the header of BOTH kinds of tab — a file tab has no *Asked* button and must still have this one — that it carries its own id rather than the capped notice’s, since two elements cannot share one and the notice’s button would stop working; that exactly ONE header control pushes itself right, because an auto left margin on two flex siblings splits the free space between them and pushes the pair apart; that it posts the SAME command the notice’s button posts, once; that it says what it will do, since the label alone reads like “open another tab”; and, in the SHIPPED minified bundle, that pressing it asks for exactly one reset. **Plus the translation freshness check the coverage test cannot make**: `bodyFor` marks an article that is MISSING and cannot mark one that is a release out of date, so all five languages are asserted to name the control — its label is English in every one of them, as every other control name in this catalogue is. Seven breaks, seven red. Covered too, but NOT in `npm test`: the reset through a REAL vendor process, by `scripts/live-fresh.mjs` — plant a number, recall it (the CONTROL, without which a model that never remembered would “forget” for reasons that have nothing to do with the reset), dispose the session and its directory as `ended` does, open a new one carrying nothing as `reopened` does, and recall again. A control on BOTH sides, which the first draft had on one: the recall before proves the model could remember, and a liveness question after proves the new session is answering at all — without it a crash, a timeout or a rate limit produces an answer with no number in it and reads as forgetting. Either control failing is NO VERDICT and a non-zero exit. Run 2026-09-14 on all three adapters: claude, codex and agy each remembered 7431 before, answered the liveness question, and said NONE after — across both `persistent` and `per-turn` shapes. **It proves the RECIPE and not `freshStart`**, which is behind `vscode`: a reset that failed to call `ended` or reused the directory would pass this every time, and what pins that half is `chatFreshWiring.test.ts` above. It is a script rather than a test because no vendor is real in CI and every run spends money on a signed-in account; what CI can check is `liveScripts.test.ts`, below |
| The two scripts that touch a REAL vendor | the call shapes, against the interface | `liveScripts.test.ts`. `scripts/live-chat.mjs` and `scripts/live-fresh.mjs` are `.mjs`, load the compiled output through `await import()` — so every symbol they touch is `any` — and `tsc` never sees them. They rot silently, and they had: `launchSpecFor`’s third parameter became a `ChatLaunch` when the model picker started deciding which model the CLI is told to use (72a6e80a, 2026-09-11) and both scripts kept passing the bare resume STRING. A string is not `undefined`, so the parameter default never applied and `modelRefusal` read `.length` of `undefined` before any process started: every turn of every vendor failed with “the model’s process could not be started”, for three days, spending nothing and proving nothing. This asserts the launch argument is an object carrying every field `ChatLaunch` declares — READ from the interface, so adding a fourth field fails here and names the scripts — that both scripts are reachable as npm scripts, and that neither can report a pass for a run that asked nothing or whose control turn failed. All three proved by breaking them, the first with the exact regression that shipped |
| The coverage gate’s one BY-NAME exclusion | yes | `sonarExclusions.test.ts`. Seventeen of the hundred and thirty-five modules in `src` import `vscode`, so no test here can execute a line of them and they reach SonarCloud as 0 % of whatever they contributed to new code — a number about the analysis rather than about the change, which is the same reason the extension’s lcov is produced at all. They are excluded from coverage BY NAME, because no glob can express “imports `vscode`”. A hand-kept list drifts silently in both directions: a new host module left out fails a pull request for a reason nobody can act on, and a module that sheds the import and stays in is production code nobody measures any more. This asserts the list is EXACTLY that set, naming the offending module and the direction; and that the host half stays a small minority, because the day it is not would mean the decisions had moved into it and the answer would be to move them back out rather than to widen the list. Both directions proved by breaking them. The whole entry comes out the day the row below exists |
| **Every command, through a real extension host** | **no** | There is no extension-host harness here yet: `@vscode/test-electron` downloads a VS Code build and runs a suite inside it, which no workflow does today. This is the largest single gap in the repository. **It is what stands between the settings lock's unit tests and its guarantee**: two hosts writing one file is the failure that started epic 2 of `PLAN_team_server_reviewer_never_called`, and the interleaving test above simulates it rather than reproducing it. |

## A scenario that drives a real child process (2026-09-09)

Two tests here launch the actual `coai-mcp --ask-remote` binary and kill it at a chosen moment. Three
rules, each of them written after the corresponding failure.

**A prerequisite is not the thing under test, and it gets its own budget.** The wait for the child to
reach the interesting moment was a flat `TimeSpan.FromSeconds(30)`, chosen once against the machine
the test was written on. On 2026-09-08 the `win-arm64` leg of the `mcp-v0.18.13` release matrix
failed at 30 s 359 ms while the other five legs passed — a cold .NET start, a sign-in, a loopback
HTTP round trip and a first write, immediately after a Release build of the whole solution, on the
slowest machine of the six. It cost the release a platform: no `coai-mcp-0.18.13-win-arm64.zip` was
ever published, and every Windows ARM install of that version answers 404. The bound is
`PrerequisiteWait`, 120 s, and it costs a fast runner nothing — the wait returns the instant the
condition holds.

**A prerequisite deadline must say what it was waiting for, and what the child was doing.** All the
evidence that failure left was `the condition was still false after 30s`, which cannot tell a slow
machine from a child that died on the way — different failures with different cures. `WaitForAsync`
takes the description and the child as REQUIRED parameters, gives up early when the child has already
exited (a dead child cannot satisfy the condition; waiting out the rest only delays the report by two
minutes), and its message names the wait, the elapsed time, the child's state as an OBSERVATION with
when it was taken, and everything the child said on stderr.

**A redirected stream must have a reader.** Both scenarios set `RedirectStandardError` and neither
drained the pipe. A child that fills a redirected pipe nobody is reading blocks on its next write —
for ever, since the parent's next act is to wait for it — and the symptom of that is a prerequisite
wait running out on one machine and not another. Whether it caused this particular failure cannot be
proved after the fact; a redirected stream with no reader is a latent hang either way. `StartShim`
drains it, which is also where the diagnostic above gets the child's own words. Its `Dispose` kills
the tree, because a wait that throws runs no `finally` of its own and a leaked `coai-mcp` holding a
claim file is how one failing test makes the next three fail for unrelated reasons.

Design record:
[PLAN_the_shim_scenario_waits_too_briefly.md](PLAN_the_shim_scenario_waits_too_briefly.md).

## The document gate's own suites (2026-09-13)

Four classes, and the split between them is the point: three prove the rules a unit at a time and the
fourth proves they are wired to each other.

| Suite | What it holds |
|---|---|
| `ADocumentRoleHasARoundTests` | The bucket selection, every case paired with its regression guard — the whole change is ONE predicate and the way to get it wrong is to widen it. Also that `plan:document` is in the catalog and in NO stage's round, asserted rather than assumed |
| `ADocumentSessionIsKeptApartTests` | Identity. The two-argument session key is pinned as a LITERAL, because what it protects is every session file already on disk. An edited document keeps its session; two files differing only in case keep their own; a case-different SIBLING is not inside where the filesystem says it is not; each gate refuses the other kind by name; an identity that looks like an ordinal is refused |
| `ADocumentIsProvenBeforeItIsReviewedTests` | Every refusal, with the resolver injected so the symlink rules are unit tests rather than a privilege the CI runner may not have. A link on a PARENT component, a sibling with the same prefix, a relative path resolved against the repository rather than the process, invalid UTF-8, a `.docx` refused before a byte is read, the size bound with both numbers in the sentence |
| `ADocumentIsReviewedEndToEndTests` | All of it at once, through the public tool, with a scripted vendor |

**Two of these rules were proved to have teeth by breaking the code and watching the failure name the
real symptom**, which is the discipline this repository holds itself to for a regression test written
after its fix. Reverting the containment check to a string prefix made `/repo-secrets/x.md` resolve to
`secrets.md` — an outside path accepted WITH a plausible identity. Reverting the path walk to resolve
only the last component made a parent-directory symlink come back `Ready` rather than `Refused`,
which is the file being read and sent to three vendors.

## The temp directory is a shared resource (2026-09-13)

Three things, and the third is the one that cost an afternoon.

**`TempDir` is the RAII helper.** `using var work = TempDir.For("coai-thing-");` and the directory is
gone when the scope ends, however the test ended. Eleven classes created one and deleted it nowhere
at all; the rest write the same delete out by hand in `Dispose`. It clears read-only attributes first,
because git marks its object files read-only and `Directory.Delete(recursive: true)` refuses one —
and it never throws on the way out, because a delete that loses a race with a held-open file is not a
failing test.

**A `using` on a value that ESCAPES its scope deletes what the caller was given.** The mechanical
conversion did exactly that to `VendorStagesTests.Repository()`, which builds a git repository and
returns its path: the round then found nothing there and answered `{ }`. A factory hands ownership
back — `private static async Task<TempDir> Repository()` — and the caller writes `using var`.

**And the suite's sweep window came down from a day to two hours**, because the cost of leftovers is
not disk. `PanelService.BuildWork` walks the temp directory looking for its own scratch, and that walk
costs the number of directories in it. Measured 2026-09-13 after a day of runs: **81,986** `coai-*`
directories, and `SubmissionOrderTests` — a hundred `BuildWork` calls in a loop — went from 8 seconds
to over four minutes, which from outside is indistinguishable from a deadlock in whatever had just
been changed. The product now walks at most once every ten minutes per process rather than twice per
reviewer. CI never sees any of this: a runner starts with an empty temp, so a green CI beside a
stalled local suite is evidence FOR this cause.

## The collector's suites (2026-09-16)

Four, and each answers a question the others cannot.

| Suite | Drives | Catches |
|---|---|---|
| `CollectorTests` | **real git** — a real squash-merge, a real deleted branch, a real orphan whose absence the fixture asserts | a walk that attributes the wrong commit; a sha that reaches git when it should not |
| `CollectRunTests` | the shipped path: `BugsQuery` out, `Collector` through, `RecordCollect` in, rows read back | a classifier that never persists — the plan round's own words |
| `TheRunsThemselvesTests` | real SQLite over a temp directory, with a clock the test moves | a sweep that ends a LIVE run; a migration that never arrives |
| `bugzSection.test.ts` | the assembled page | a section that renders but never repaints; a text box that would flicker under the caret |

**Why real git rather than a fake.** Every failure this guards against is git's — a commit no ref
reaches, a file that moved, a history rewritten under the finding — and a fake would assert what we
*believe* about those. Measurement kept correcting the belief: the guard was written as equality
once and inverted the whole feature, and the first orphan rate came back 90.7 % against a branch
that happened to be 488 commits behind.

**Why a clock the test moves.** The heartbeat sweep is about elapsed time, and the alternative is a
test that sleeps for thirty-one minutes, which is a test nobody runs. `RoundsDb.Open` takes a
`TimeProvider` for exactly this: a clock a test cannot control is a column a test cannot assert.

**What each of them would still miss, and what covers it.** A database test and a markup test can
both pass while the built CLI, the migration and the webview wiring disagree — so the JSON contract
between the halves is exercised against the **real binary's** output, and `parseBugs` is asserted
against an OLD server's answer (no `lastRun` key) as well as a new one. That pairing is the lesson
of every wire field this product has shipped out of step.

## The review page's suites (2026-09-16)

| Suite | Drives | Catches |
|---|---|---|
| `ThePairsThemselvesTests` | real SQLite | an upsert that forgets a decision; a pair written for a claim that lost |
| `WhatIsStoredIsAnonymousTests` | the real normaliser, then the COLUMN | a skeleton computed correctly and stored wrong |
| `ThePairModesTests` | both one-shot modes | a request fault answered 64 instead of 65; a malformed document reported as success |
| `ARankingIsNotTrustedTests` | the pure ordering | a model that invents, omits, duplicates or contradicts |
| `bugzReviewPage.test.ts` | the page RUN against a DOM shim | a tick-box that renders and selects nothing |

**Why the page is run rather than read.** `PROJECT.md` refuses a new behavioural assertion over page
source text, and story 4 earned that ruling: a model picker matched every regex written about it
while being wired to nothing. The boxes the test drives are built from the ids the page ACTUALLY
rendered — a hand-written fixture handed to the shim would pass with no checkbox on the page at all,
which is the mistake its first version made.

**What these still do not prove.** Nothing spawns the built binary and drives the review flow end to
end; `bugzLiveContract.test.ts` does that for the corpus read and there is no equivalent for the
pairs. And the ranking has no transport, so `Ranking.Order` is exercised and nothing produces a real
reply for it to order.

## The ingest server's suites (2026-09-16)

| Suite | Drives | Catches |
|---|---|---|
| `TheAlphabetTests` | the real normaliser over this repository's OWN files | a whitelist too narrow (it found two) or too wide |
| `TheKeywordsAreOneListTests` | the grammars | a keyword list gone stale after a grammar bump |
| `TheIngestTests` | the pure decisions | a refusal that strands its neighbours; a leak that is stored |
| `TheRouteTests` | the REAL server in-process | the AOT JSON binding, the bearer header, the 401, the caps |
| `OnlyThreeFieldsLeaveTests` | the mapping and the serialiser | the symbol, the id or the finding's prose crossing |
| `BothHalvesTests` | the REAL client against the REAL server | the two halves disagreeing about an id, a word, or a document |
| `TheBuiltBinariesTests` | two real PROCESSES over a real socket | a publish, trimming or embedded-resource defect; an exit code |

**Why an HTTP suite when the decisions are already unit-tested.** `Ingest.Take` is pure and covered;
the route is not part of it. The one that matters most is the AOT JSON binding — this repository has
a contract suite because such a failure once made a released `coai-server` answer 500 to everything,
and a serializer context compiles perfectly while binding nothing.

**Why the alphabet is tested over real files.** A fixture proves what its author imagined. Run over
this repository's own methods it found two defects in the validator — adjacent empty strings read as
a leak, and a C# range operator read as a numeric literal — both of which refused good work.

**The live cross-implementation check.** `BothHalvesTests` seeds a real `coai.db`, hosts the real
`coai-bugs` in-process, and runs `UploadRun.RunAsync` against it. It was the loudest finding of the
code round and the convention is explicit: *a contract with two implementations has ONE live check
that exercises them against each other — two suites agreeing with the same file is not that check.*
The wire types are shared now, which removes most of the drift it was written to catch; what it
still catches is what a shared record cannot promise — that the client derives the id the server
derives, that it reads the words the server writes, that both AOT serializers bind the other's
documents, and that a queue past the 200-pair cap arrives in full rather than reporting success
about its first batch. Before it, `UploadRun.RunAsync` had no test at all; only its private mapping
did, and the catalogue said otherwise.

**And one level below that: two real processes.** `BothHalvesTests` hosts the server's ASSEMBLIES, which cannot see a publish-layout, trimming or embedded-resource defect — and this story's whole reason for existing is a keyword file read from a directory no release has. `TheBuiltBinariesTests` starts the built `coai-bugs` on a real port, mints a key through the real `--issue-key`, uploads through the real `coai-mcp --upload-pairs`, and reads the result back through the real `--waiting`. It is on `COAI_CONTRACT_EXE`, the seam the release workflow already sets, so the same scenario is a fast check here and the release smoke there.

**It found two defects the first time it ran.** `coai-bugs --rotate-the-moon` started Kestrel and listened for ever instead of exiting 64 — the binary had no unknown-mode branch at all, which is the half of the exit-code rule that lets a caller detect an old binary. The test noticed after four minutes and fifty-seven seconds, which is how long it takes to see that a process nobody asked to start is still running.

**What these still do not prove.** The no-client-IP promise is a deployment fact — a reverse proxy
writes `remote_addr` before the request reaches any route — and no test in this process can reach
it. It is verified by reading the deployed stack's logs after a real ingest.

## What this does NOT prove

The most valuable section, and the first one people drop.

- **No vendor is real in CI.** The server scenarios drive a **fake CLI** (`CommandFixtures`,
  `FAKECLI_*`) scripted per round. So the composition is proven — dedup across vendors, a standing
  rejection surviving a round boundary, a partial round still producing a verdict — and the vendors'
  actual behaviour is not. That is what the campaign harness is for, and it is why campaigns are run
  by hand against real subscriptions.
- **No model quality is asserted anywhere.** A green suite says the gate works, never that a review
  was good. Quality is measured, not tested: `research/RESULTS_*.md`.
- **No GPU in CI.** The `local` arm never runs there; an Ollama or vLLM endpoint is exercised only in
  a hand-run campaign.
- **No extension host in CI**, per the table above — so a defect that needs the real `vscode` API to
  appear (a command wired to the wrong handler, a webview CSP change) is caught by installing the
  build and using it, which is exactly how the 0.18.1 SQLite defect was found.
- **No LAYOUT anywhere, so no CSS behaviour is ever observed.** The page harness executes a page's
  SCRIPT against a hand-written DOM shim; the shim has no layout engine, so `scrollWidth`,
  `clientWidth`, `getBoundingClientRect` and computed styles are whatever the test set them to. What
  a CSS change can therefore be tested for is that the DECLARATION is in the stylesheet — which
  `chatPage.test.ts`'s `rules()`/`ruleFor()` parser reads as data — and that a rule which must NOT
  inherit something says so. That a long unbreakable run actually wraps, or that the conversation has
  no horizontal scrollbar, is **not** proven here and cannot be without a real browser. The wrap fix
  of 2026-09-15 (issue #299) is asserted exactly this far and no further: `.msg .what` and `.passage`
  carry `overflow-wrap: anywhere`, `.msg .what pre, .msg .what table` carry `overflow-wrap: normal`,
  and the `pre` keeps `overflow-x: auto` without gaining `white-space: pre-wrap`. A test that
  inserted a long string and asserted "no overflow" against the shim would assert something the shim
  decided, and would look like evidence while proving less than the declaration does.
- **One platform per run.** The suites run on the CI matrix; the AOT publish covers six RIDs and the
  release smoke exercises the binary on every RID whose machine can execute it — but `osx-x64` is
  cross-built on an arm64 runner and is not executed there.
- **No concurrency between two people.** Two sessions reviewing the same branch on one machine is a
  real case (it happens daily here) and there is no scenario for it; the pieces are covered by
  `SessionStoreConcurrencyTests` and `SessionSaveSurvivesReadersTests`.
- **No two extension hosts appending to one ledger.** Every VS Code window is its own extension
  host and they share the coai data directory, so `chat-usage.jsonl` has as many writers as the
  person has windows open. A unit test cannot fork four writers and mean anything by the result, so
  this one was MEASURED rather than tested: `npm run measure:append` (`scripts/measure-append.mjs`)
  forks real processes that append records of deliberately awkward sizes — the paddings straddle a
  pipe buffer, a page and a filesystem block — and checks every line back, verifying each record's
  padding LENGTH so that two halves of two records cannot pass merely by parsing. Eight processes ×
  1000 records, 116 MB including 60 KB lines: **8000 whole records of 8000, zero torn**, on
  Windows 11 / NTFS / node 22, 2026-09-10; four × 500 was clean first. It exits non-zero if the file
  ever tears, so the claim can be re-checked on another filesystem rather than believed.

Adoption build checks, before reconciliation with main: Debug and Release solution builds
completed with zero warnings/errors. Each configuration's MTP runners passed: MCP 1136/1137
(one explicit Windows directory-link privilege skip), bench 113/113, Team server 226/226.
The generator invalidation guard was removed temporarily: the missing-source test failed
because the old output remained; restoring the guard made it pass. This validates the
failure behavior, not merely that a build hook was declared.

After reconciliation with main, commit `63b40fc`: both configurations built with zero
warnings/errors and passed MCP 1141/1142 (the same platform skip), bench 113/113 and Team
server 226/226. Extension: 1340/1341 plus three generator scenarios. A fresh WSL clone
initialized the committed neutral pin, resolved it and read six complete TypeScript-scope
sources from a nested directory. A separate worktree restored the complete pre-adoption
commit and initialized its legacy mount with a clean tree. Native Claude's TypeScript cell
exceeded the bounded trace size and remains incomplete; details and measured scope are in
[shared-rules-adoption-smoke.json](shared-rules-adoption-smoke.json).


The first completed code review read 14 rule files (77,577 bytes), with 15 omissions and no
missing mount; all 12 reviewers answered. The old installed collector had shown only the
root adapters. Review follow-ups reproduce a stale temporary file blocking the next build
(EEXIST), an older local `review-gate.md` incorrectly hidden by the current shared body,
and serialized candidate reads. Each regression was observed red, green with its fix, then
red with the production behavior removed. Missing-mount detection now reuses the collected
paths; this is an enumeration refactor verified by the existing RuleFiles suite, not a
claimed latency measurement. Detailed decisions are in `shared-rules-adoption-review.json`.


Review corrections on the pinned S2 source (2026-09-10): extension 1342 total / 1341 passed /
1 skipped, plus 3 generator cases; client/server contract 3 passed; settings seam passed.
Debug and Release MCP: 1142 total / 1141 passed / 1 Windows directory-link privilege skip; bench 113 and
Team server 226 passed in both configurations. A Release build first found an all-zero
intermediate core reference DLL (CS0009); rebuilding that project restored its managed
metadata, and the solution then built with zero warnings/errors. This was a local build
artifact failure, not a source-code fix. Package 0.32.0 contains the canonical v5 marker and
neutral/local discovery paths; installation remains separate from this artifact observation.


After reconciling main through 897c7fa, the extension suite reports 1349 total / 1348 passed /
1 skipped. Windows and WSL each pass all three generator scenarios. The WSL run first
exposed a fixture error: a POSIX node_modules symlink is not ignored by the source's
node_modules/ directory pattern and made a clean fixture look dirty. The fixture now copies
the two locked packages into a real directory; pin validation remains unchanged.


The pre-PR-feedback local artifact was extension 0.32.1 (0.32.0 was an uninstalled validation package
before the last main reconciliation). Its packaging suite reports 1348 passed / 1 skipped.
The Windows Native AOT MCP artifact is 0.18.16-sharedrules.20260910: it opened a real SQLite
database and passed all five stdio contract cases through COAI_CONTRACT_EXE. The first
publish failed because vswhere.exe was missing from PATH; the already-installed Visual
Studio Installer directory was added only to the publishing process's environment. No SDK
installation, persistent host setting, or Team-server deployment was performed.


PR #192 follow-up (2026-09-10): the discovery fixture includes Rust and a legacy-only
shared mount. Generator scenarios live under `src/test/prepareGate.test.mjs` and still run
through npm test. The canonical-body comparison uses this checkout's fixed test layout,
so it cannot borrow a parent repository's mount when its own is missing.
The full suite exposed an unrelated timing assumption: the stdin echo test killed its real
child after 300 ms, before a response arrived (`[]` instead of `echo:hello`). It now waits
for an actual reply before killing the child, with the existing ten-second completion
ceiling and cleanup. All 18 process-launcher scenarios then passed.

Local package 0.32.2 contains exactly the 5,934 UTF-8 bytes of the canonical gate body.
Verification read `extension/dist/extension.js` from the VSIX, decoded its single gate
literal with the installed TypeScript scanner (`typescript/unstable/ast/scanner`), and
compared it to the pinned rule after LF normalization and frontmatter removal. Package
hashes are in `shared-rules-adoption-smoke.json`; no model-behavior claim follows from this.
The subsequent echo-test correction changes test source only, which is excluded from the
VSIX; the already-built package's runtime bundle is unchanged.

After the echo synchronization correction, the full normal npm test run reports
1,349 total / 1,348 passed / 1 skipped, plus all three generator scenarios. The updated
Release RuleFiles executable reports 22 passed. Family pin, lifecycle and checklist-shape
checks pass. These are local checks; PR CI and installed-host behavior are separate.

Final main reconciliation through `dea70a1` preserved its Windows/WSL chat changes and
resolved its legacy gitlink update by retaining the same SHA at the neutral mount. The
normal packaging run passed 1,397 extension tests / 1 skip, plus all three generator cases.
Package 0.32.3 was independently checked for all 5,934 canonical body bytes, as above;
its immutable hashes replace the earlier uninstalled validation package in the evidence JSON.

Local installation on 2026-09-10: VS Code installed extension 0.32.3; its actual installed
bundle matches the VSIX SHA. MCP was atomically replaced with the verified Native AOT
artifact, preserving the previous binary; SQLite already matched and was not rewritten.
All five McpContractTests then passed with COAI_CONTRACT_EXE pointing to the installed
globalStorage executable. Existing editor/agent processes were not restarted, so the
manual panel scenario and an already-running host reloading remain unverified.

## The roles page, RUN — and its harness (2026-09-16)

`rolesPageHarness.ts` is the DOM shim and the runner, extracted out of `rolesPageScript.test.ts`
the moment `editRolesInTabs.test.ts` needed the same thing: a second shim is two shims that drift,
and the one thing a shim must be is the same for everybody asserting against it. It takes the
nodes `querySelectorAll` should answer with, keyed by selector — a test that presses a tab needs
the page to FIND the other tabs and the sections, and a shim answering every selector with nothing
would let a broken switch look exactly like a working one.

`editRolesInTabs.test.ts` covers issue #293 and executes what it can: the page's own click handler
for the tab switch, and `nextTab` for the transition the host applies. The two assertions that are
not executions are deliberate and say so — there is no CSS engine here, so `.prompt.mine` being
absent from the stylesheet is invisible to a class-name assertion, and the test reads the generated
rule as well as the class. `roleTone.test.ts` is about what the palette SAYS; the check that there
is only ONE palette renders the same roles through `panelHtml` and `rolesHtml` and compares them,
because a test of the module alone stays green while either renderer keeps a private copy.
