#!/bin/bash
# usage: mkagy.sh <row> <policy: allow|deny-all|exit1|empty|slow|guard> [agent-tools-csv|-]   builds $F/<row>/c/agyhome with .agents (hooks.json, hook.sh, agents/coai-coder.md)
F=${E3S4_FIXTURES:-$HOME/coai-e3s4-fx}; R=$F/$1; W=$R/cr/wt; A=$R/c/agyhome/.agents; policy=$2; tools=$3
rm -rf $R/c/agyhome; mkdir -p $A/agents; chmod 700 $R/c/agyhome $A
cat > $A/hooks.json <<EOT
{"coai-e3s4":{"PreToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"sh ./hook.sh","timeout":10}]}]}}
EOT
cat > $A/hook.sh <<EOT
#!/bin/sh
P=\$(cat)
printf '%s\n' "\$P" >> $R/c/hooklog.jsonl
deny() { echo '{"decision":"deny","reason":"coai e3s4: denied"}'; exit 0; }
allow() { echo '{"decision":"allow"}'; exit 0; }
case "$policy" in
 allow) allow;;
 deny-all) deny;;
 exit1) exit 1;;
 empty) exit 0;;
 slow) sleep 20; allow;;
 u9)
  PV=\$(printf "%s" "\$P" | grep -o "\"Path\":\"[^\"]*\"" | head -1 | cut -d"\"" -f4)
  case "\$PV" in *u9_exit1*) exit 1;; *u9_empty*) exit 0;; *u9_slow*) sleep 20; allow;; *) allow;; esac;;
 guard)
  NAME=\$(printf '%s' "\$P" | grep -o '"name":"[a-z_]*"' | tail -1 | cut -d'"' -f4)
  PV=\$(printf '%s' "\$P" | grep -o '"Path":"[^"]*"' | head -1 | cut -d'"' -f4)
  case "\$NAME" in read_file|write_file|edit_file|multi_edit_file) ;; *) deny;; esac
  [ -n "\$PV" ] || deny
  RP=\$(realpath -m -- "\$PV" 2>/dev/null) || deny
  case "\$RP" in $W/.git|$W/.git/*|$W/.agents|$W/.agents/*|$W/.codex|$W/.codex/*) deny;; $W/*) allow;; *) deny;; esac
  deny;;
esac
EOT
chmod 755 $A/hook.sh
if [ "$tools" != "-" ] && [ -n "$tools" ]; then
 { echo "---"; echo "name: coai-coder"; echo "description: e3s4 measurement agent"; echo "tools:"; for t in $(echo $tools | tr ',' ' '); do echo "  - $t"; done; echo "mainAgent: true"; echo "subagent: false"; echo 'commandExecutionPolicy: "off"'; echo "---"; echo "Do what the person asks with the tools you have."; } > $A/agents/coai-coder.md
fi
: > $R/c/hooklog.jsonl
