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
  vendorEnabled:
    'Whether this reviewer takes part. Switching one off keeps its settings — the next round simply runs without it.',
  vendorModel:
    "Which model this vendor reviews with. Empty means the CLI's own default, which is usually its newest. A stronger model finds more and costs more; the panel exists so you can mix.",
  vendorBaseUrl:
    'The OpenAI-compatible endpoint this vendor is reached at. Its API key lives in the CredsForDevs config entry under this vendor’s name, never here.',
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
  escalationMinutes:
    'How long a question waits for your answer before the AI is told to ask you in the chat instead. The question stays open in this panel either way — nothing is decided by your silence.',

  credsKey:
    'The CredsForDevs config-entry key that unlocks the vendor API keys. It is a pass to one vault entry — revocable, and useless while VS Code is closed — not a secret itself. Vendors whose CLI is signed in need no key at all.',
} as const;

export type HelpKey = keyof typeof HELP;
