# A timed-out ssh call ends its whole tree on Linux and macOS

> Status: TODO, not planned. Found 2026-11-25 by the code gate of the accounts plan's E1 (coai session `a4a016fb`,
> finding 0, rejected there as out of that epic's scope).

## The finding

The rule says: launch external processes as exe + argv, always with a timeout, and the timeout kills the whole process
tree. `processLauncher.ts` `killTree` does that on Windows only (`taskkill /T`). On Linux and macOS it calls
`child.kill()`: only the `ssh` child gets the signal. A child of `ssh` — for example a `ProxyCommand` from the person's
own `~/.ssh/config` — can outlive a timed-out call.

It is older than the accounts plan: the session reader (`runRemote`, the terminal-chat plan's E2, #982) has used this
launcher since 2026-11-12, and the accounts round (`runAccounts`) uses the same one. `SECURITY.md` says "we kill the
ssh child", which is what the code does.

## What a fix needs

- Start the child in its own process group on POSIX (`detached: true`) and signal the group (`process.kill(-pid)`) on
  a timeout and on a cancel, then the child.
- Check what a new process group does to `ssh`'s own terminal handling (`BatchMode`, no tty) and to the other callers
  of `launch` with `killTree: true`.
- A test that a grandchild is gone after the timeout, on Linux and macOS CI.
- `SECURITY.md`: "we end the ssh call's process group".
