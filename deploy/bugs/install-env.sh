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

# TWO lines, from stdin: the server's secret, then the administrator list. `head -c` bounds what the
# caller can send; `tr -d` removes the carriage return a Windows-written secret box adds, which would
# otherwise become part of the value and hash differently from the one the client thinks it sent.
#
# THE SECOND LINE MAY BE EMPTY, and that is not a failure: a server with no administrators answers
# 401 to every /admin call, which is a legitimate way to run this one — it says so in its own startup
# log. What would be a failure is guessing, so the line is always written, empty or not.
INPUT=$(head -c 65536)
SECRET=$(printf '%s\n' "$INPUT" | sed -n '1p' | tr -d '\r')
ADMINS=$(printf '%s\n' "$INPUT" | sed -n '2p' | tr -d '\r')
[ -n "$SECRET" ] || { printf 'no secret arrived on stdin\n' >&2; exit 1; }

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
printf 'the environment file is written\n'
