#!/bin/sh
# Write /etc/coai-bugs/env from one line on stdin. Runs as ROOT, through one sudoers line.
#
# ---------------------------------------------------------------------------
# WHY THIS IS A SEPARATE FILE, AND WHY IT IS INSTALLED RATHER THAN RUN FROM THE CHECKOUT.
#
# The environment file must be `root:coai-bugs 0640`: the service reads it, and the deploy account
# must not be able to read back a secret it was only ever meant to deliver. Writing it therefore
# needs root, and `coai-bugs-deploy` is deliberately not root.
#
# `deploy-cmd.sh` lives in `/opt/coai-bugs/src`, which the deploy account OWNS — that is what lets
# a fix to the wrapper reach the host with the next deploy. A root helper in that same checkout
# would turn the same property into a privilege escalation: anybody who could write the checkout
# could write a file root then executes. So this one file is COPIED to `/usr/local/sbin`, owned by
# root, and the sudoers line names it there and forbids arguments.
#
#   install -m 0755 -o root -g root deploy/bugs/install-env.sh /usr/local/sbin/coai-bugs-install-env
#   coai-bugs-deploy ALL=(root) NOPASSWD: /usr/local/sbin/coai-bugs-install-env ""
#
# The trailing `""` is not decoration: a sudoers command with no argument spec permits ANY
# arguments. This one takes none, and the policy says so.
#
# Being a copy, it is the one file here that does NOT update itself with the checkout. It has no
# reason to change — it reads a line and writes a file — and the README says to reinstall it if it
# ever does.
# ---------------------------------------------------------------------------
set -eu

ENV_FILE=/etc/coai-bugs/env

# WHAT THIS COPY UNDERSTANDS, declared so the checkout can ask before it sends anything.
#
# Being a copy under /usr/local/sbin, this file can fall BEHIND the checkout that talks to it, and on
# 2026-09-22 it had: the host was still running the version before the administrator record existed,
# which read one line, discarded the key list in silence and reported success. The deploy went green
# and delivered nobody. `deploy/bugs/helper-protocol.sh` reads this line out of the installed file
# and refuses the delivery when it is not the one it needs; `deploy-cmd.sh` runs that check first.
#
# Spelled ONCE, here, and demanded once, there. Bump the number whenever what arrives on stdin
# changes meaning — that is the whole event this guards against — and reinstall the helper.
#   1  one record:  the secret
#   2  two records: the secret, then the base64 administrator list
PROTOCOL='coai-bugs-install-env protocol 2'

# TWO RECORDS, from stdin: the server's secret, then the administrator list. `tr -d` removes the
# carriage return a Windows-written secret box adds, which would otherwise become part of the value
# and hash differently from the one the client thinks it sent.
#
# READ, not `sed -n 2p`, because the two cases have to be told apart and only `read` can: an EMPTY
# second line means "no administrators", which is a legitimate way to run this server, and an ABSENT
# one means the caller sent a message this script does not understand — an old workflow, a truncated
# transfer, somebody running `secret` by hand. The first writes an empty value on purpose. The second
# would SILENTLY REMOVE every administrator, print success, and leave a server answering 401 to every
# admin call, which is the exact failure this whole story exists to end. (Code round, codex, twice.)
#
# `|| ADMINS=''` is not decoration either: `read` returns non-zero at EOF and this script runs under
# `set -e`. (Plan round, gemini.)
IFS= read -r SECRET || SECRET=''
SECRET=$(printf '%s' "$SECRET" | tr -d '\r')
[ -n "$SECRET" ] || { printf 'no secret arrived on stdin\n' >&2; exit 1; }

# THE VALUE DECIDES, and the status only breaks the tie. `read` returns non-zero at EOF, which is how
# an ABSENT record is told from an EMPTY one — but a record that arrived WITHOUT a trailing newline
# also returns non-zero while setting the variable, and refusing that would be refusing a delivery
# that is perfectly complete. So: read it, and ask whether anything came. (Code round, codex.)
ADMINS=''
if IFS= read -r ADMINS; then
  ARRIVED=yes
elif [ -n "$ADMINS" ]; then
  ARRIVED=yes
else
  ARRIVED=no
fi

if [ "$ARRIVED" = no ]; then
  printf 'the administrator line is missing. Send two lines: the secret, then the base64 key list (empty for none)\n' >&2
  exit 1
fi

ADMINS=$(printf '%s' "$ADMINS" | tr -d '\r')

# NOTHING AFTER IT. A base64 value pasted without `-w0` wraps at 76 columns, and everything after the
# first chunk would arrive here as a third line and be thrown away — leaving a value that decodes to
# a PREFIX of the list. The first key would work, later administrators would not, and the deployment
# check, which tests the first key, would pass. (Code round, codex.)
#
# The same tie-break as above, for the same reason in reverse: an unterminated third chunk sets EXTRA
# and returns non-zero, and taking the status alone would have discarded it in silence.
EXTRA=''
if IFS= read -r EXTRA || [ -n "$EXTRA" ]; then
  printf 'more than two lines arrived. The key list must be base64 on ONE line: `base64 -w0`\n' >&2
  exit 1
fi

# Bounded, as `head -c` used to bound it, but as a REFUSAL rather than a truncation: a value cut in
# half is a value that decodes to nonsense.
[ "${#ADMINS}" -le 65536 ] || { printf 'the administrator line is longer than 64 KiB\n' >&2; exit 1; }

install -d -m 0750 -o root -g coai-bugs /etc/coai-bugs
umask 077
TMP=$(mktemp)
trap 'rm -f "$TMP"' EXIT

# Both values in ONE file so the daemon and every maintenance command read the same database.
# Setting the data directory in the unit and not here is how keys get issued into a file the server
# never opens, and every upload is then rejected with a 401 nobody can explain.
# The administrators travel BASE64 — the list is newline-separated and an EnvironmentFile assignment
# cannot hold a newline. Base64's alphabet is `A-Za-z0-9+/=`, so the value needs no quoting here and
# systemd reads it back byte for byte; the server decodes it and refuses anything else, because a
# server that fell back to the raw text would start with the WRONG administrators and say nothing.
{
  printf 'COAI_BUGS_SECRET=%s\n' "$SECRET"
  printf 'COAI_BUGS_DATA=/opt/coai-bugs/data\n'
  printf 'COAI_BUGS_ADMIN_KEYS=%s\n' "$ADMINS"
} > "$TMP"
chown root:coai-bugs "$TMP"
chmod 0640 "$TMP"
mv -f "$TMP" "$ENV_FILE"
trap - EXIT

# NEITHER VALUE is printed, and the administrator count is not printed either — it would be one more
# thing in a CI log that says something about the credentials. A helper that confirmed them would put
# them there.
printf 'the environment file is written (%s)\n' "$PROTOCOL"
