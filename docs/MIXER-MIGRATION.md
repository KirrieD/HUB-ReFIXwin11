# QuietMixer migration in HUB 1.1

The installer only detects the existing installation. It leaves the executable,
data, process and HKCU startup value intact and writes
`%LOCALAPPDATA%/ReFIXHub/migration-quiet-mixer.json`:

```json
{
  "LegacyPath": "C:\\Users\\example\\AppData\\Local\\QuietMixer\\QuietMixer.exe",
  "RunValue": "\"C:\\Users\\example\\AppData\\Local\\QuietMixer\\QuietMixer.exe\" --background",
  "WasRunning": true
}
```

The marker is a pending request, not evidence that migration succeeded. Runtime
accepts only the known executable below the current user's LocalAppData, with no
links in that path. The captured `QuietMixer` Run value must still match exactly.
Supported registrations are the quoted executable with `--background` (the
original application's format), quoted executable alone, or unquoted executable
alone. Other arguments and paths are deliberately rejected.

`LegacyMixerMigration.RunAsync()` first verifies the bundled package and SHA256.
`ModuleManager.InstallLocalAsync` never falls back to a release download, even if
the bundled package disappears after preflight. Only then does migration request
legacy shutdown using its own `--exit` command and bounded waits. It never kills a
legacy process. Executable identity is checked before targeting a running process.

Migration stops an active older HUB mixer before an update, installs the verified
local package, and starts the adapted mixer through ModuleManager's identity and
control-pipe health verification. It saves `StartMixerWithHub=true`, rechecks the
exact old Run value, removes that value, and clears the pending marker. Existing
standalone executable and data remain available.

Failures before startup-value removal leave the marker and legacy startup value
in place, stop a newly started adapted mixer where possible, restore the previous
startup preference, and restart the legacy mixer if migration stopped it and the
marker recorded it running. Exceptions include the original failure and any
rollback failure so the host can display/log an accurate result. If migration
committed but marker cleanup fails, the error explicitly reports that migration
succeeded; it does not restart a duplicate legacy mixer.

The host must await migration before its ordinary mixer auto-start path. A
migration failure must be surfaced and must suppress auto-start for that attempt.
Ordinary startup must only start an installed, verified mixer and never install or
download it implicitly.

`ILegacyMixerEnvironment` isolates registry access, process discovery, graceful
shutdown and restart. `IMixerMigrationModule` isolates module operations for
deterministic rollback tests. `MigrationTests.Run` uses temporary directories and
fake legacy boundaries: it never touches the user's Run key or legacy processes.
An additional test uses the real ModuleManager to verify local hash checking,
offline installation and preservation of an installed version after tampering or
a missing package. Its placeholder executable is never launched.
