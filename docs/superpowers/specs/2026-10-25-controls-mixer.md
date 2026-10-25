# Mixer integration and keyboard controls 1.1

User requests completing transfer of QuietMixer into HUB and the GitHub repository, with full lifecycle/style/startup integration, plus a HUB settings section Управление / Назначение клавиш displaying existing assignments and allowing edits.

Preserve existing module protocol and shared theme. Ship version 1.1.0 for hub and both modules so existing 1.0 installations can update. Publish source to the user's named repository if credentials allow; prepare and trigger a release only after verification. Never force-push an existing branch. Do not delete the original standalone source or user notes.

## Keyboard contract

Shared namespace ReFix.Shared, new Controls.cs:
- `ControlAction`: Id, Name, Scope, DefaultGesture, Description public properties. Scope `global`, `hub`, `quiet-mixer`, `quick-note`.
- Static `ControlActions.All` immutable list: hub.toggle Ctrl+Alt+H global; quiet-mixer.toggle Ctrl+Alt+V global; quick-note.toggle empty global (optional); hub.hide Esc hub; quiet-mixer.hide Esc quiet-mixer; quick-note.hide Esc quick-note; quick-note.save Ctrl+S quick-note.
- `ControlsProfile`: Dictionary<string,string> Bindings, bool StartMixerWithHub; `Get(string id)`, `Clone()`. Defaults included for missing keys.
- `KeyChord`: static `Parse(string)` (empty disables), `string Gesture`, `uint Modifiers`, `uint VirtualKey`, `bool Matches(System.Windows.Input.Key, ModifierKeys)`, display canonical Ctrl/Alt/Shift/Win names. Recognize letters, digits, function keys and normal navigation. Global assignments must include Ctrl or Alt; reject OS-reserved gestures and lone modifiers. Layout-independent VKs; editor shows physical Latin/Russian labels.
- `ControlsStore(string root) : IDisposable`: `Current` cloned profile; `Changed` and `RuntimeChanged` events raised off-UI; `Assign(string actionId,string gesture)` validates internal conflicts and probes global availability; `Save(ControlsProfile)` atomic, validates full profile; `ResetDefaults()`; `GetRuntime(string actionId)` returns verified live HotkeyRuntimeStatus or null. Read controls.json with safe file sharing and watch it without polling. Runtime file watcher separate from config event.
- `HotkeyRuntimeStatus`: ActionId, Requested, Actual, Error, ProcessId, ProcessStartTicks. Validate PID/start-time when reading. Stale files must not appear active.
- `GlobalKeyRegistration(ControlsStore store,string actionId,IntPtr hwnd,int baseId,Action pressed) : IDisposable`: registers current key; `ProcessMessage(int msg, IntPtr wParam)` handles WM_HOTKEY and own posted rebind message. Rebind on config event via PostMessage to owning UI thread. Reserve replacement before unregistering old using alternating IDs, preserve old key on conflicts and report actual/requested/error via scoped state files. Dispose unregisters and removes only its own status. No hidden timers/polling, no WPF Application required for cold tray startup.

Local handlers match profile keys, rather than constants. Ctrl+S and Esc are contextual, so Esc may repeat across different windows; global gestures cannot shadow local ones. Standard slider navigation (arrows, Tab) is documented as standard controls, not falsely configurable.

## Host UI / integration

Separate modeless ControlsWindow, themed like HUB, accessible from sidebar Управление and tray. Heading Назначение клавиш, action rows grouped by global/local scope, actual registration/error state, capture new gesture, disable and reset defaults. Capture Escape cancels; unmodified Backspace/Delete clears. Show a keyboard diagram with physical assigned keys highlighted and Latin/Russian letter pairs. Display current sidebar/tray shortcuts, not stale hardcoded strings. Settings update both already-running modules.

Root Program owns one ControlsStore and GlobalKeyRegistration for hub.toggle. Module processes own their global registrations and local handlers; opening/stopping a module therefore acquires/releases its key. Optional quick-note global key defaults empty. Disabled/uninstalled module keys are shown as not active. Controls UI probes OS conflicts before save and reports late registration conflicts from runtime files.

Retain mixer startup behavior: controls flag StartMixerWithHub; start only installed verified mixer, no background download. User enabling/disabling mixer from HUB updates the desired startup flag; settings can explicitly override it. Start module processes with --background; explicit Open shows it. Hook and audio refresh retain prior safeguards.

## Migration

Installer can detect only the known legacy %LOCALAPPDATA%/QuietMixer/QuietMixer.exe registration. Mark a pending migration, and new HUB handles graceful legacy stop -> local verified mixer install/update -> start and health check -> remove only exact matching old startup value. Keep old executable/data. On failure preserve legacy registration and report; do not silently kill an unrelated process. Runtime migration must be isolated/testable and never run during tests on the real user profile. No independent startup toggle in adapted mixer.

## Delivery

Version-aware build/catalogue/package/install scripts and release workflow supporting v1.1.0 (not hardcoded first-release-only). Catalogue hash must match the exact published archive. Keep original 1.0 modules until new install succeeds. Existing shared theme/user data retained. Tests cover conflicts, duplicate scopes, rebind with old key retained, external config changes, runtime identity, actual 1.1 mixer+note lifecycle, local key updates, migration failure/success with isolated state and no real user registry writes.
