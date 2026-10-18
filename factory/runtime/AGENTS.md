# Runtime scope

Read [README.md](README.md) for role policy. Add networking lifecycle or shell
guides only when changing their use of that policy.

- ListenServer is both server and client; DedicatedServer has no local player.
- RuntimeContext is role policy, not a transport connection-health check.
- Preserve shell-owned mode transitions; no client-controlled authority switch.
