# new_design — the Settings page, rebuilt around one model catalog (mockup)

A static, clickable mockup of the redesigned **ConnectOtherAIs › Settings** page. It is for looking
at and arguing about. It is not shipped: nothing here is part of the extension, the vsix, or
coai-mcp, and nothing in it talks to them.

## Open it

Double-click `index.html`. It needs no server and no build, and runs from `file://` in any current
Chrome, Edge or Firefox. The **Theme** switch in the top bar previews dark and light, and the
**Design notes** tab holds the investigation behind the design.

Your edits are kept in this browser's local storage, so a reload keeps them. **Reset demo data**
puts the demo setup back.

## What is real and what is an outline

| Tab | State |
|---|---|
| **Models** | Built. Add (by where the model runs), duplicate, remove (with what it breaks), switch on/off; filter rows by use, effort and vendor, plus access and text; per-instance effort, system prompt, connection, price and timeout. |
| **Setup** | Built: Vendor keys (every key any model uses, against the vault), Team servers (add, sign in/out, what each offers, its models, remove), MCP server (installed vs published, AI clients and the gate instructions, the data folder), This side (per-side settings, export/import). |
| **Reviews** | Built: Stages & roles (four stages, each with its models, roles, rounds and threshold; deal lenses/roles; Fast/Full), Prompts per round (a picker per round of each role that is on), The gate (when rounds run out, orders to the calling AI, the caller's own models, commands), Limits (with the round limit worked out live). |
| **Consultants** | Built: Consultant (who answers each caller — a same-vendor pick is *shown*, never refused — its confinement and its Check; budgets; cadence; the prompt), Question consultant (when it is asked, rows of model + prompt admitted by the measured capability table, base prompts, folders, limits). |
| **Security lane** | Built after `todo/PLAN_the_security_tab_reads_at_a_glance.md`: the general prompt first, cards marked default / edited / custom, conditions collapsed to one line, model ticks per card, pairs with source and token budget. |
| **Chat** | Built: which model a chat opens on (with its starting text), sending (prompt, language, who presses send), prompt presets, shortcuts. |

**No page but Models picks a model.** Each shows a *Models* strip of the instances ticked for it, with a link
to Models filtered to that use.

Three rules the page follows everywhere:

- **Every page has text size (🔍) and brightness (☀)** in its header.
- **Blocks side by side are one height.** Cards are rows of one shared grid (CSS subgrid), so the same
  section of two neighbouring cards starts on the same line.
- **"new" lasts a week.** A control's `new` tag shows for the first 7 days after the update that
  brought it. Use *Days since this update* in the top bar to preview day 0 and day 7.

The demo data is the operator's setup as of 2026-10-04: two Codex instances, Antigravity, two
Claude, a local engine, GLM 5.3 low and high, Grok and a Team server instance.

## Files

| File | What it holds |
|---|---|
| `index.html` | The page shell and the Design notes text |
| `styles.css` | VS Code Dark/Light Modern tokens. Two columns from 1100 px, one below |
| `data.js` | Vendors, features, access kinds, effort vocabularies, availability rules, demo data |
| `app.js` | State, tabs, the Models tab and its add dialog; the page registry the other modules join; the shared "?", "server too old" and confirm helpers |
| `models.js` | The Models card's world-facing parts: the CLI and its actions, coai-mcp's verdict, where a list came from, what an API model runs at, local-engine safety, removal guards |
| `triggers.js` | How a change is matched to security checks — a copy of coai-mcp's SecuritySignals |
| `*.css` per module | `models.css`, `reviews.css`, `asking.css`, `lanes.css`, `setup.css`, loaded after `styles.css` |
| `reviews.js` | Reviews: Stages, Roles & prompts, Prompts per round, The gate, Commands, Limits |
| `asking.js` | Consultants: Consultant, Question consultant |
| `lanes.js` | Security lane, Chat |
| `setup.js` | Setup: Vendor keys, Team servers, MCP server, This side |
| `check.mjs` | Drives the page in headless Chrome, asserts the main flows and saves screenshots |

## Check it

```bash
node new_design/check.mjs            # prints PASS/FAIL per flow, exit 1 on any FAIL
```

This needs Node 22+ (it uses the built-in WebSocket) and Chrome. Set `CHROME=<path>` to use another
Chromium build. Screenshots go to `%TEMP%/coai-mockup-check` unless you pass a folder.

## What happens next

1. The operator reviews this mockup, and we fix it until it is right.
2. Then a plan is written in `todo/`, on the coai gate. It covers the catalog setting, migrating
   today's settings, the coai-mcp wire, effort in the Claude, Codex and Team server adapters, and the
   system prompt.
3. Only then does development start.
