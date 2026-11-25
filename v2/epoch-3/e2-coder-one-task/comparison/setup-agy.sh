#!/bin/bash
# usage: setup-agy.sh   builds the agy coder's guard folder (cwd of every launch, bound READ-ONLY into the coder unit, hidden from the checks), the pinned program copy
# and the sign-in COPY's place. Shape: RESULTS_agy_coder.md section 2 (AG1) + the judgement's G2-G7: a hook that always prints an explicit decision, the
# agent file with a tool list and commandExecutionPolicy off, the working directory = the guard folder.
set -euo pipefail
. "$(dirname "$0")/env.sh"
v=agy; W=$(job_tree $v); C=$(job_root $v); G=$CMP_SCRATCH/copies/agy/guard; A=$G/.agents
rm -rf "$G"; mkdir -p "$A/agents"; chmod 700 "$G" "$A"
# the pinned program: a COPY of the measured release in a folder of its own (a self-update of the person's agy cannot change it; a read-only bind cannot be updated)
if [ ! -x "$CMP_BASE/prog/agy/agy" ]; then mkdir -p "$CMP_BASE/prog/agy"; cp "$HOME/.local/bin/agy" "$CMP_BASE/prog/agy/agy"; fi
echo "agy program: $(sha256sum "$CMP_BASE/prog/agy/agy" | cut -c1-12) vs real $(sha256sum "$HOME/.local/bin/agy" | cut -c1-12)"
cat > "$A/hooks.json" <<'EOT'
{"coai-cmp3":{"PreToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"exec sh ./hook.sh","timeout":10}]}]}}
EOT
cat > "$A/hook.sh" <<EOT
#!/bin/sh
# Guard (G2/G3/G4): prints EXACTLY one explicit decision for every input, including an empty one; allows only read_file / write_file / edit_file
# whose Path, resolved through links, lies inside the worktree and not under .git, .agents or .codex. Everything else is deny. One record per decision.
LOG=$C/coder/home/.gemini/coai-hooklog.jsonl
decide() {
  P=\$(cat) || return 1
  [ -n "\$P" ] || return 1
  NAME=\$(printf '%s' "\$P" | grep -o '"name":"[a-z_]*"' | tail -1 | cut -d'"' -f4)
  PV=\$(printf '%s' "\$P" | grep -o '"Path":"[^"]*"' | head -1 | cut -d'"' -f4)
  printf '%s\t%s\t%s\n' "\$(date -u +%FT%TZ)" "\$NAME" "\$PV" >> "\$LOG" 2>/dev/null
  case "\$NAME" in read_file|write_file|edit_file) ;; *) return 1;; esac
  [ -n "\$PV" ] || return 1
  RP=\$(realpath -m -- "\$PV" 2>/dev/null) || return 1
  case "\$RP" in $W/.git|$W/.git/*|$W/.agents|$W/.agents/*|$W/.codex|$W/.codex/*) return 1;; $W/*) return 0;; esac
  return 1
}
if decide; then D='{"decision":"allow"}'; else D='{"decision":"deny","reason":"coai cmp3 guard: denied"}'; fi
printf '%s\n' "\$D"
printf '%s\t%s\n' "\$(date -u +%FT%TZ)" "\$D" >> "\$LOG" 2>/dev/null
exit 0
EOT
chmod 755 "$A/hook.sh"
cat > "$A/agents/coai-coder.md" <<'EOT'
---
name: coai-coder
description: coai coder (comparison run)
tools:
  - view_file
  - write_to_file
  - replace_file_content
  - multi_replace_file_content
mainAgent: true
subagent: false
commandExecutionPolicy: "off"
---
Do what the person asks with the tools you have.
EOT
chmod 644 "$A/agents/coai-coder.md" "$A/hooks.json"; chmod -R go-rwx "$G"
find "$G" -type f -printf '%m %P\n'
