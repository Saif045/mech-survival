# Diagnostics scope

Read [README.md](README.md) for logging and relay contracts. Add test/operator
protocols only when running processes; add historical evidence only for the
failure under investigation.

- Local logs survive relay/transport failure; host alone writes master.jsonl.
- Derive remote sender from MultiplayerApi, bound backlog/batches, reject duplicates.
- Never log relay transport recursively or call Godot print APIs inside logger callbacks.
- Diagnostics acknowledgements are not gameplay delivery guarantees or rollback state.
