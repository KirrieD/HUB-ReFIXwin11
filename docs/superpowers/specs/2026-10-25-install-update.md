# ZIP, Setup and HUB updates

User found HTTP404 on catalogue refresh in1.0 and requests two delivery modes and accept/cancel self-update with automatic restart. Preserve mixer migration and configurable keyboard work. Publish exact catalogue and assets together; no bogus URLs or empty remote file.

Version remains1.1.0 (first public binaries). Core executable becomes HUB-ReFIX.Core.exe. HUB-ReFIX.exe is a small independent .NET/WinForms bootstrap without Shared reference so missing core/shared/config/catalogue/payload files can show `Требуется настройка для корректной работы`. Bundle checks use bundle-files.json SchemaVersion1/Version/Files[{Path,Sha256}], strict safe relative names and bounded JSON. Setup writes installation.json marker with Mode=Setup and Version; successful startup footer is `Все установлено`. Portable has no registry changes until explicit install/startup useraction. First installed1.0 requires one manual upgrade because its binary has no updater yet.

Setup.exe uses the same bootstrap source in setup mode with embedded exact portable ZIP resource HubPayload.zip; presents installer UI, verifies/extracts payload, stops only same user's existing HUB gracefully, installs to %LOCALAPPDATA%/ReFIXHub/app, preserves user data, registers quoted Bootstrap --background startup/shortcut and legacy mixer marker, launches bootstrap. Tests must not install on realprofile; offer --extract-to <isolateddirectory> for payload verification without registry/launch.

Self-updater namespace ReFix.Hub:
- `HubRelease` Version,PackageUrl,Sha256,SetupUrl,SetupSha256,Notes.
- `PreparedHubUpdate` plan representation/path.
- `HubUpdateService(string dataRoot,string appDirectory)`: `Task<HubRelease> CheckAsync()` (null when none/newest nothigher), `Task<PreparedHubUpdate> PrepareAsync(HubRelease release)` (download SHA/safeextract/manifest check), `void StartApply(PreparedHubUpdate prepared)` (copy trusted helper outside target, verified parent PID/starttime, launch helper; caller exits afterward).
- Read latest GitHub release metadata at fixed repo API, find asset update.json; bounded HTTPS fetches, validate repo asset URLs and permitted GitHub signed CDN redirects. No auth/token needed public reads. No network polling: one startup check + explicit check button; optional user-show throttle6h, no hidden repeatingtimer.
- update.json schema1 includes Version,PackageUrl,Sha256,SetupUrl,SetupSha256,Notes. Generated from actual finished portable ZIP and Setup.exe and uploaded with them.

ReFix.UpdateHelper.exe standalone executable no Shared DLL dependency. Copy it to staging then launch --plan <json>. Wait verified parent PID/starttime/path to exit without forcedkill; safe ownedfile backup/copy, preserve unknownfiles/settings, retain Setup marker on installed updates. Verify all hashes/paths again before writing. Roll back touched files and restart old Bootstrap if copy/install fails; success launches updated Bootstrap. No writes outside validated target/staging/backup roots. Test apply/rollback against isolated fake apps, no real installedHUB changes.

Root UI: visible check/launch status and menu `Проверить обновления`; prompt new version `Обновить` / `Отмена`; download/progress UI, acceptance required before download; apply => exit => helper restart. Cancellation leaves current application unchanged. Automatic check must not initialize WPF merely for no-update result. MainUIthread handles prompt via posted message to NativeWindow; background thread performs only I/O.

Build: Shared -> Core -> Bootstrap -> UpdateHelper -> modules -> portable ZIP -> Setup embedded ZIP -> update.json actual hashes. Workflow after prepared catalogue calls packaging + setup/metadata then publishes ZIP,Setup,moduleZIPs,catalog,update.json; sync exact CI catalogue to main AFTERrelease. Prepare-Release must precede ZIP construction so embedded catalogue URLs/hashes match assets. Build packaging twice must not leak tests/unknown files.
