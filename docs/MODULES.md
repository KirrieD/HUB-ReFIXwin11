# Modules

The bundled catalogue contains two local process modules, not remote downloads:

- quiet-mixer: a copied QuietMixer adapted to the shared theme and control SDK.
- quick-note: a local text note saved under Paths.Root/notes/quick-note.txt.

Build the shared SDK first, then run:

    powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Modules.ps1

The script uses installed Roslyn (or the Framework compiler fallback), C# 5 and Windows .NET Framework 4.8/WPF references. It creates artifacts/packages/<id>-1.0.0.zip and artifacts/catalog.json. Every archive contains its executable, ReFix.Shared.dll and module.json. Catalogue SHA256 values are computed from the completed ZIPs. PackageUrl is empty: the host resolves the matching bundled archive locally. Publishing a release and adding a real validated URL is a separate explicit action.

Run either EXE normally to show it. The host passes --background to start a hidden process. Shared ModuleRuntime accepts status/show/stop; callbacks marshal to the WPF dispatcher. Status is provided by the shared runtime and does not imply that a hidden window is visible. Stop exits the process and removes its shared runtime state.

Both modules use ThemeStore.Current and ThemeApplier. Theme events update resources without opening windows. The mixer keeps its current rows and sliders during theme changes; resource updates never refresh, rebuild or write audio channels. Open WPF context menus share the same resources. The mixer uses its existing visible-only audio refresh timer; hiding releases audio and stops the timer. Quick Note has no idle timer. It saves on Ctrl+S, deactivation, hide and runtime stop; X hides it, and the hub can show or stop it later. Unsaved note errors remain visible in its footer.

Both modules derive their single-instance identities (and the mixer listener name) from ModuleRuntime.PipeName, sharing its current-user and data-root isolation. This copy has its own single-instance name and does not register a separate login startup entry. HUB controls startup. It retains the original mixer hotkey and shell interaction, so running both the original and adapted mixer simultaneously can compete for the hotkey; the adapted copy reports that failure through its existing tray notification. The original QuietMixer directory is untouched.

## Extending the catalogue

Keep IDs lowercase ASCII letters/digits/hyphens and versions filesystem-safe. Process modules should include the shared assembly beside their executable and start a ModuleRuntime with their catalogue ID. UI callbacks and ThemeStore.Changed require dispatcher marshaling. Do not add hidden polling loops. Persist module data beneath Paths.Root, which honors REFIX_DATA_ROOT for isolated testing.

Persistent fixes are intentionally not bundled: no requested system change has been selected. A fix package must implement separate status, enable and disable commands and a bounded JSON response describing actual verified state. Status must inspect the target setting instead of trusting a marker. Enable must first back up the exact previous value (including whether it existed), apply only the documented change and verify it. Disable must restore the backup and verify restoration. Keep backups beneath Paths.Root/state, detect conflicts instead of overwriting subsequent user changes, and return an error with an explanatory message if verification fails. Installation must never execute enable automatically. Match the exact JSON schema and argument contract documented in SDK.md before publishing a fix package.
Add -Test to build and run hidden-window regression checks (no audio device writes or shown windows). These cover live resource updates, unchanged slider values/rows, open-menu resource dictionaries and disabled hidden audio refresh.

Both module windows use transparent WPF chrome with one alpha background layer, a separate image-only blur layer, and an outer frame glow. Window.Opacity remains 1 so foreground controls stay readable. Corner radii come from the shared profile. Image crop is recalculated only on theme events and window size changes. Quick Note has a draggable header and an explicit save-and-hide button.
