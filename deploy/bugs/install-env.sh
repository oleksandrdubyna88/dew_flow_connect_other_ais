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

# One line, from stdin. `head -c` bounds what the caller can send; `tr -d` removes the newline a
# shell pipeline adds, which would otherwise become part of the secret and hash differently from
# the one the client thinks it sent.
SECRET=$(head -c 4096 | head -1 | tr -d '\r\n')
[ -n "$SECRET" ] || { printf 'no secret arrived on stdin\n' >&2; exit 1; }

install -d -m 0750 -o root -g coai-bugs /etc/coai-bugs
umask 077
TMP=$(mktemp)
trap 'rm -f "$TMP"' EXIT

# Both values in ONE file so the daemon and every maintenance command read the same database.
# Setting the data directory in the unit and not here is how keys get issued into a file the server
# never opens, and every upload is then rejected with a 401 nobody can explain.
{
  printf 'COAI_BUGS_SECRET=%s\n' "$SECRET"
  printf 'COAI_BUGS_DATA=/opt/coai-bugs/data\n'
} > "$TMP"
chown root:coai-bugs "$TMP"
chmod 0640 "$TMP"
mv -f "$TMP" "$ENV_FILE"
trap - EXIT

# The VALUE is never printed. A helper that confirmed it would put it in a CI log.
printf 'the environment file is written\n'
