#!/bin/bash
# usage: setup-codex.sh   writes the job's CODEX_HOME config.toml (the profile of RESULTS_codex_coder.md section 2) into the scratch COPY home, and the flat schema into the job folder.
. "$(dirname "$0")/env.sh"
H=$CMP_SCRATCH/copies/codex/home; PD=$(dirname "$(readlink -f "$HOME/.local/bin/codex")")
cat > "$H/config.toml" <<EOT
model = "gpt-6.1-terra"
model_reasoning_effort = "low"
default_permissions = "coai"

[permissions.coai.filesystem]
":minimal" = "read"
"$PD" = "read"
":project_roots" = { "." = "write" }
EOT
chmod 600 "$H/config.toml"
cp "$(dirname "$0")/coder-report.flat.schema.json" "$(job_root codex)/coder/report.flat.schema.json"
sed "s#$HOME#~#g" "$H/config.toml"
