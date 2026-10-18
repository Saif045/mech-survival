# A/B harness scope

Read [README.md](README.md), [testing protocol](../../docs/testing-protocol.md)
and [operator protocol](../../docs/runtime-test-operator.md) before executing or
changing host/VM acceptance. Load [investigation protocol](../../docs/investigation-protocol.md)
only for an authorized investigation after a terminal attempt.

Freeze build/config during an attempt, use fresh immutable evidence, launch in
dependency order, record PASS/FAIL/BLOCKED, preserve logs and verify host/VM cleanup.
Launch/Verify/Stop/Retry are universal PowerShell commands; the harness reports
infrastructure/evidence, while gameplay acceptance needs assertions and human
visual evidence. Never restart Steam or reconfigure the VM inside a live attempt.

Interactive acceptance uses the permanent checkout and installed host task.
After moving tools, rerun `host/install_host_task.ps1` before the next session.
No automatic A/B session is needed for source path changes alone. Keep context
narrow; do not load unrelated subsystems or historical runs by default.
