/**
 * What each setting actually means, in a sentence a person can act on.
 *
 * <p>Written because "Per vendor" explains nothing: a label is a reminder for someone who already
 * knows, and every one of these was chosen by somebody who did not yet. Each entry says what the
 * setting does AND why it exists — a number whose purpose is invisible gets set to whatever looks
 * biggest.</p>
 *
 * <p>Kept apart from the markup so the wording is reviewable on its own, and so a missing entry is
 * a compile error rather than a blank tooltip.</p>
 */
export const HELP = {
  perSideSettings:
    'One machine can hold several working environments - a local window, and each WSL distro or '
    + 'remote host. VS Code resolves these settings from the settings.json of the CLIENT and hands '
    + 'the same values to every one of them, so without this switch three companies share one proxy, '
    + 'one set of CLI paths and one vault key. On, each side keeps its own values, seeded from what '
    + 'it had when you switched it on - so nothing changes until you edit something, and switching '
    + 'off and on again does not discard what a side had configured. Your text size and help language '
    + 'stay shared: they belong to you rather than to the work.',
  autonomous:
    'The gate tells the assistant to work without interrupting you: a question that does not block it is written down and asked at the END, all of them together; a question that does block it is asked at once — but only after it has re-read what it has written and gathered every other blocking question, so you are interrupted once with all of them instead of five times with one each.',
  splitPlan:
    'After a plan passes, the gate tells the assistant how big the plan is, from its build steps and its length: small enough to build as it stands, 3-5 stories, or 2-3, 3-4 or 4-5 epics of stories — fewer when the work is smaller, never more. That is a heuristic, and the assistant is told the numbers and that it may disagree in writing. How often the work comes back through the gate is the choice below — once per epic or once for the whole task, never once per story. The order is given ONCE per assistant: an epic coming back for its own plan review is told it is a piece — build it as a single unit and say so if it is genuinely too big — rather than being told to split again, which would have no end.',
  gatePer:
    'How often split work comes back through this gate. ONE GATE PER EPIC: each epic on its own branch, starting from the previous epic’s commit, gets one plan round and one code round over its whole diff, and is committed as one commit — its stories are not gated one by one. ONE GATE FOR THE WHOLE TASK: the plan round you just passed is the only plan gate; everything is built on one branch, each epic committed as one commit, and one code round runs over the whole diff at the end. Either way nothing is gated per story: a gate session ends at its code round, so a round per story cost a new branch and two rounds for every story.',
  cadence:
    'When the consultant is asked although nobody is stuck. After a plan is split into epics, every group of epics — three by default — owes one consultation BEFORE its first code round: is this group right, where is it weak, what did it forget. From five epics the assistant is also asked which epics and stories carry the most risk, and each one it names gets a consultation of its own. OFF: no orders. REMIND: the order is in every review reply and nothing is refused. REQUIRE: the group’s first code round is refused until its consultation has been taken and closed with an outcome. A consultant that cannot be reached never jams the work: the round goes through, with the reason written on it.',
  consultEnabled:
    'Lets an AI working with this gate ask ANOTHER vendor’s model for a second opinion — a consultant that reads this checkout read-only, with the uncommitted change, and answers advice the AI must verify. Three kinds exist: STUCK, when the AI itself is not getting out; CADENCE, when the gate orders one for a group of epics; RISK, when it orders one for a piece the AI named as risky. Off switches off all three: the AI is told no consultant can be had, and a cadence set to Require stands its refusal down, with the reason written on the round.',
  consultCaller:
    'Which vendor answers when THIS kind of AI consults, and on which model. A different vendor from the caller is the point: a model cannot see its own blind spot. Empty model is the vendor’s own default. These are the consultant’s settings alone — a reviewer row of the same vendor shares only its name and its key in the vault.',
  consultBaseUrl:
    'The address of the vendor’s API, for a vendor this build talks to over HTTP rather than through its CLI. Empty uses the vendor’s own public endpoint.',
  consultExecutablePath:
    'Where the vendor’s command-line tool is. Empty looks it up on PATH, which is what you want unless it is installed somewhere PATH does not reach.',
  consultTurns:
    'How many question-and-answer turns one consultation may have — five unless you change it — for EVERY kind: stuck, cadence and risk alike. The last turn closes it; a follow-up after that is refused and a fresh problem statement opens a new one. It bounds a conversation that circles.',
  consultCallsPerSession:
    'How many STUCK consultations one assistant session may open in 24 hours — ten unless you change it. It exists because the failure it prevents is an AI calling the consultant forty times. Cadence and risk consultations are NOT counted: the gate orders them, and each group of epics or risky piece can have only one, which bounds them instead.',
  consultIdleMinutes:
    'A consultation nobody has come back to for this long is closed and its vendor conversation dropped — checked every minute while the server runs, for every kind. A later question is a new consultation with the whole budget. An ordered one that closed this way stops counting for its group until someone records how it ended.',
  consultPrompt:
    'The instructions the consultant receives ahead of the problem. Empty is the prompt this build ships with; what you type is written to the server’s prompt override and used from the next consultation. Restore default takes the override away.',
  cadenceEvery:
    'How many epics share ONE consultation — three unless you change it. The groups are counted from the plan’s own first epic, so a plan that continues another at epic 5 is grouped 5-7, 8-10; the gate names the group in its order, and the AI calls exactly what it names.',
  cadenceRiskThreshold:
    'From how many epics the AI is also asked which epics or stories carry the most risk — five unless you change it. Below it, the groups alone are consulted on.',
  cadenceRiskMax:
    'The most risky pieces one plan may name — three unless you change it. Each one named gets a consultation of its own before its epic’s code round, so this bounds how many ordered consultations a large plan can add.',
  stopLocalWhenQuiet:
    'When every cloud reviewer of a round has answered and they found at most one remark between them — any severity — the local reviewer finishes the launch it is on and starts no more. The round lists the local roles it did not start, with the reason, and the server log says the cloud found little and the local reviewer stood down. A cloud reviewer that failed keeps the local one running: its silence is not "found little". A round reads the switch when it STARTS, so turning it on or off takes effect from the next round. Off unless you turn it on.',
  splitWithFable:
    'The split itself is done by the calling assistant’s STRONGEST model — deciding what the epics and stories ARE is the judgement that shapes everything after it — and the risky stories go back to it: payments, money, authentication, security, architecture, data migration. The ordinary ones run on its implementation model. Both are models of YOUR assistant, not reviewers in the list above, and each kind of assistant gets its own: pick them in the rows below the switch. Claude Code is told Fable and Opus unless you change them; Codex, Gemini and any other client are told to use their own strongest model until you name one, never another vendor’s.',
  chatPrompt:
    'What the selected passage is sent with. One word — Explain — unless you change it, and the box '
    + 'is several lines high because a word is not always enough: "explain this to somebody who knows '
    + 'C# but not Rust" is a different question from "explain this". The passage itself always arrives '
    + 'below your instruction, fenced and marked as material, so a paragraph that reads like an order '
    + 'is treated as text rather than obeyed.',
  chatLanguage:
    'The language the OTHER AI answers in. Deliberately not the language of these help pages: that '
    + 'one is English on most machines, and borrowing it would deliver English explanations — which '
    + 'is the one thing this feature exists to avoid. English by default, because the models are best '
    + 'at it; set it to yours if a dense English answer is what sent you here.',
  chatAutoSend:
    'Who presses send. The keybinding copies the selection itself, one keystroke earlier, so it can '
    + 'be trusted to be the passage you meant — it asks straight away. The right-click menu cannot '
    + 'copy for you (closing the menu takes the selection out of the panel), so it takes whatever you '
    + 'last copied and fills the box for you to send, because it has no way to know how old that is. '
    + 'Always sends on both paths; Never fills the box on both.',
  chatModel:
    'Which reviewer answers a chat. Empty means the first one that can, which is what most people '
    + 'want. Only reviewers on a runtime the chat can speak to appear here; any other configured '
    + 'reviewer is listed underneath with the reason, rather than quietly missing — a picker with a '
    + 'gap in it cannot tell you whether it is a bug or a policy.',
  vendorStages:
    'Which stages this reviewer is asked. Measured over fourteen judged runs: a local model was '
    + '19 % useful on a plan and 3 % on code, while writing more findings than both hosted vendors '
    + 'together - so "on for the plan, off for the code" is a real setting rather than a knob. Both '
    + 'boxes are ticked unless you untick one; the row checkbox above still turns the vendor off '
    + 'everywhere.',
  vendorDocuments:
    'Whether this reviewer is asked to read DOCUMENTS - the roles you marked as not programming '
    + 'work. On a Team server this is the box that decides whether the document leaves this machine: '
    + 'it is sent to your company server and reviewed there on the shared subscription, so it is off '
    + 'until you turn it on, whatever the other two boxes say. On a reviewer that runs here it '
    + 'follows the plan box until you change either of those two, at which point it keeps what it '
    + 'had.',
  vendorEnabled:
    'Whether this reviewer takes part. Switching one off keeps its settings — the next round simply runs without it.',
  vendorModel:
    "Which model this vendor reviews with. Empty means the CLI's own default, which is usually its newest. A stronger model finds more and costs more; the panel exists so you can mix.",
  vendorBaseUrl:
    'The OpenAI-compatible endpoint this vendor is reached at. Its API key lives in the CredsForDevs config entry under this vendor’s name, never here.',
  apiModel:
    'Which model this API reviews with, typed exactly as the endpoint names it — the panel does not spend a paid call per repaint to list them, and a list shipped with this extension would be wrong for every endpoint you can point the row at. `coai-mcp --probe-api --vendor <this row>` lists the ids your key can actually call; an empty model is refused by the endpoint with a 400 on every review, and the row says so before a round does.',
  apiDialect:
    'How the request is spelled for this endpoint’s family — which of temperature, seed and the frequency penalty travel, the name of the token ceiling, whether the finding schema is demanded or merely asked for. `openai` is the generic one and sends nothing a reasoning model is documented to refuse. A vendor’s own dialect is added only once it has been measured against the real endpoint, never typed from its documentation, which is why this list is short.',
  vendorExecutablePath:
    'Where this vendor’s CLI is, for when PATH cannot be trusted to answer. Empty means "look it up on PATH", which is right almost everywhere — and wrong in WSL, where codex and gemini resolve to the WINDOWS npm shims through the interop PATH and die on a missing Linux binary. Put the native path here and the round stops depending on which shell happened to start the server.',

  runVendor:
    'Open this vendor’s own CLI in a terminal, with its usage command ready at the prompt — press Enter to see what you have spent. This is also where you sign a CLI in: a vendor whose CLI is not authenticated fails every round with a timeout.',

  localModel:
    'Which model on this machine reviews. The list is what the engine reports right now, with its parameter size, quantisation and disk size — not a list shipped with this extension, because what is installed is a fact about your machine. Empty means whatever the engine answers with when asked for no model in particular. A review needs room: the prompt, the plan or the diff and the schema all go in one request, so a small context window is refused by the engine rather than answered badly.',

  localPrice:
    'Left here for accounting, and normally left empty: a model on your own hardware has no token bill. What it costs is electricity and the card being busy, neither of which this panel can see. Fill these in only if you want the spending chart to price local runs anyway — at a rate you invent.',

  reprobeLocal:
    'Ask the engine again what it has. A successful probe is cached for a minute so the panel is not listing models on every repaint — which means a model you just pulled, or an engine you just started, is not there yet. This is the button for that. It was left out of the first version as "a CLI’s button", and the gate reviewing this feature pointed out that a cache with no way to clear it is a stale list with no way out.',

  fixWslNetwork:
    'An engine is answering on the Windows side of this machine and this WSL distro cannot reach it — a distro\'s own 127.0.0.1 is not the Windows host\'s. This writes networkingMode=mirrored into the Windows .wslconfig, after showing you exactly what it will write; then WSL shares the host\'s interfaces and 127.0.0.1 is the right address with nothing else configured. It cannot restart WSL for you — that is `wsl --shutdown` from Windows, and it would terminate the distro this extension is running in. Press it again afterwards to put the setting back: it is global to every distro, docker-desktop included, and some VPN clients dislike it.',

  localEndpoint:
    'The OpenAI-compatible base of a local engine, ending in /v1. Leave it empty to use whatever the probe found on this machine — Ollama on 11434, a vLLM on 8000. Fill it in for anything else: a vLLM on another port, a model server on the network, or an engine the probe cannot see. The probe URL and this base are NOT the same address: Ollama serves its own API at the root and its OpenAI-compatible surface under /v1, and a base without it fails at the first review with a 404 that reads like a model problem.',

  vendorPrice:
    'What this vendor bills per million tokens, in and out. From YOU, never from a table this product ships: a shipped price list is wrong for anyone on a flat subscription, wrong the first time a vendor changes a price, and wrong silently in both cases. Only Claude reports its own cost; codex and antigravity report tokens and nothing else, so their money read as a dash until you fill these in. What is computed from your rate is marked with a tilde — ~$0.42 is what the tokens work out to, $0.42 is what a vendor actually charged, and the totals keep the two apart.',

  installVendorCli:
    'Install this vendor’s CLI. It opens a terminal with the exact command typed and waiting, picked for the OS the terminal will actually run in — PowerShell on Windows, the shell in your distribution when VS Code is attached to WSL — plus how to get node first if this machine has none. Only the vendors’ own published sources: npm for Codex, Google’s own script for Antigravity. A fresh WSL box has none of these CLIs, and the answer being on somebody else’s docs page is why a reviewer never gets added.',

  addVendor:
    'Add another independent reviewer: a preset, or any OpenAI-compatible endpoint by name and URL. More vendors means more independent eyes — and more cost per round.',

  roleEnabled:
    'Whether this role reviews at all. Unticked, it takes no part in a code round — no reviewer is launched for it, nothing it would have found is counted, and it lends the stage neither its rounds nor its threshold. Its rounds, threshold and prompt picks are kept and come back unchanged when you tick it again, so this is a switch rather than a way of clearing the box. Plan review is not affected: it has one role and no switch.',
  lastRole:
    'The only role still ticked. A code round with nobody in it is not an empty round — the server counts a round no reviewer answered as unresolved, so it would sit open and the next review would be refused for the wrong reason. Tick another role first, then this one can go.',
  dormantRole:
    'Switched off on the roles page, which is a different switch from this one. Two reach every role: this tick, which is about this side’s settings, and the role’s own Active, which is about the catalog every side shares — and the server reads both, so a role needs both to run. This box would write only the first and the role would stay off, so it is inert until the roles page switches the role back on. Edit roles… is at the foot of this section.',
  maxRounds:
    'How many times THIS ROLE may be asked before the policy below takes over. Each role has its own count, because they are not worth the same number of passes: architecture may deserve two with different lenses while performance deserves one, and a shared budget makes the cheapest role pay for the most expensive. A round runs the roles that still have a count left — all of them, not only the ones that gated, because the next round reads a REVISED diff and a role that was clean on the old one can find something in the fix. When a role’s count is spent it simply stops being asked, and the stage keeps going for the roles that have not.',
  gateThreshold:
    'The gate opens for THIS ROLE when this many of its findings are left, or fewer — a finding is counted against the threshold of the role that raised it, so a noisy role cannot spend another role’s tolerance. Only blocking and major findings count, and the same defect raised by two vendors counts once. Zero demands a clean review from this role; two tolerates a couple of disagreements. The stage passes when every role is at or under its own number.',
  codeWorkspace:
    'What a code reviewer is given. Fast sends the composed prompt — the diff, the plan and this project’s written rules — in an empty directory, so an agentic CLI has nothing to wander into; Full also hands it the checkout of the commit under review. Fast is the default because it was measured rather than preferred: on one commit every hosted model found MORE useful defects without the checkout — four to eight, six to ten, six to seven — at a half to a third of the input tokens, with no wrong finding from any of them, and three real defects surfaced that no run WITH a checkout had reached. The exploring was costing findings rather than buying them. Choose Full when the review genuinely needs the surrounding code — a change whose meaning depends on callers the diff does not show.',
  onExhausted:
    'What happens when the rounds run out and findings still gate. Ask a human: the review stops and waits for you. Continue: it proceeds and says out loud what is still open, touching none of them. Good enough: the AI reads what is open, applies the findings that are true and useful, rejects the rest with reasons, and moves on. Climb the ladder: raise the reviewers’ effort, then their model, then the arbiter’s — and try again.',

  maxConcurrency:
    'How many reviewer processes may run at the same time, across every vendor. Higher finishes a round sooner and loads the machine harder; each one is a full CLI.',
  maxPerProvider:
    'Of those, how many may belong to ONE vendor. Rate limits are per vendor: without this cap a single slow or throttled vendor would hold every slot and the others would wait behind it.',
  reviewerTimeout:
    'How long one reviewer may take before its process is killed and the round records a timeout for it. A round with a missing reviewer still produces a verdict, and says who was missing.',
  roundTimeout:
    'How long a WHOLE round may take before the reviewers still running are cancelled and the round is gated on whatever answered. Leave it at 0 and it is worked out from the round itself: vendors x roles reviewers through the machine cap above, each wave allowed one reviewer timeout. At the defaults that is four waves, forty minutes. Lower it and you are cutting into reviewers that have not finished; their findings are lost, and the verdict is made without them.',
  escalationMinutes:
    'How long a question waits for your answer before the AI is told to ask you in the chat instead. The question stays open in this panel either way — nothing is decided by your silence.',

  credsKey:
    'The CredsForDevs config-entry key that unlocks the vendor API keys. It is a pass to one vault entry — revocable, and useless while VS Code is closed — not a secret itself. Vendors whose CLI is signed in need no key at all.',
} as const;

export type HelpKey = keyof typeof HELP;
