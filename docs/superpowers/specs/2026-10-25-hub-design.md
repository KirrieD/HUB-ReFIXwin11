# HUB ReFIX / Windows 11

Build the user's desktop widget hub for small Windows 11 utilities and fixes. Local repository is O:\COMPONTARIUM\HUB-ReFIXwin11, remote https://github.com/KirrieD/HUB-ReFIXwin11.git (initially empty). Do not publish or push without a separate request.

## Behavior

- Scrollable module catalogue with name, description, version, actions, live NO/OFF/ON, and a small built-in preview of that module's window.
- NO: no verified installed package; OFF: installed but inactive; ON: process responds to its control pipe or a persistent fix's status command verifies it is applied. Errors explain why a state could not be verified; never show ON just because the install button was clicked.
- Download/update packages from this GitHub repository's releases through an HTTPS catalogue, SHA-256 verification, bounded ZIP extraction, staged installation and rollback. Network only on explicit refresh/install/update. Cached catalogue works offline. No arbitrary automatic fix execution.
- Shared styling across hub, style editor, preview panels and opted-in visual modules. System mode derives OS light/dark, accent and accessibility settings. Custom mode supports RGB/hex colors, glow color/intensity/radius, background alpha, image fit/position/opacity/blur, corner radius and font size. Local image is copied to the user's asset directory. Changes preview immediately and persist with a short write debounce; no hidden render loops.
- Style editor is a separate modeless owned window. Scrolling/interacting with HUB remains possible. Switching to an unrelated application hides HUB; selecting its own style editor keeps HUB open. X hides to tray; Exit explicitly quits the host. Ctrl+Alt+H toggles it; tray icon opens it. User-login startup is installed via a script and switchable in UI.
- Hidden state has no UI/status polling. IPC, filesystem notifications, process exit events and foreground-window events drive updates. A visible-only status timer is acceptable for health verification; stop it on hide. No network polling.
- Ship the existing QuietMixer as first adapted utility and a small sample module demonstrating the SDK. Keep the original O:\COMPONTARIUM\QuietMixer untouched. No invented downloads for unreleased packages; bundled packages are honestly shown as bundled/local.

## Architecture decisions

C# 5 + Windows-provided .NET Framework 4.8, WPF and WinForms tray, no NuGet dependencies. WPF was chosen for shared native styling and accessible controls over Electron (larger idle footprint) and native C++ (missing SDK and much more custom UI code). This does not promise zero RAM; measure actual idle use. Modules are separate processes, not dynamically loaded plugins. Persistent fixes use status/enable/disable commands with JSON state output, bounded waits, and explicit user actions.

User data defaults to %LOCALAPPDATA%\ReFIXHub; REFIX_DATA_ROOT overrides it for tests. Module installs live in modules/<id>/<version>, state records in state/, styling in theme.json. Package archive contents must remain inside their staging root and cannot write arbitrary files. Keep repository/tag URLs constrained to KirrieD/HUB-ReFIXwin11. .NET runtime code and downloads remain unsigned unless a publisher certificate is supplied; hashes provide integrity against the catalogue, not independent publisher authenticity.

## Visual direction

A compact Windows utility shelf: left navigation with brand mark, main module rows with a real state badge and window preview, unobtrusive footer for repository state. Segoe UI Variable/Segoe UI, clear Russian copy, system colors by default. Default dark palette: background #202020, surface #2D2D2D, text #F3F3F3, secondary #A8A8A8, border #414141, accent from Windows. Custom styling can override the palette. No continuous decorative animation; short transitions only on direct interaction and disabled for reduced-motion settings.

## Shared API contract

Namespace `ReFix.Shared`, assembly `ReFix.Shared.dll`:

`Paths.Root` (honors REFIX_DATA_ROOT), `Paths.ThemeFile`, `Paths.Modules`, `Paths.State`, `Paths.Ensure()`.

`ThemeProfile` public properties: `Mode` (System/Custom), `Background`, `Surface`, `Foreground`, `Muted`, `Accent`, `Border`, `Glow` (all #RRGGBB), `BackgroundOpacity` (0..1), `GlowOpacity` (0..1), `GlowRadius` (0..40), `CornerRadius` (0..24), `FontSize` (11..20), `ImagePath`, `ImageOpacity` (0..1), `ImageBlur` (0..30), `ImageFit` (UniformToFill/Uniform/Fill), `ImageX`, `ImageY` (0..1). `Clone()`, `Normalize()`, static `Resolve(ThemeProfile)`.

`ThemeStore : IDisposable`: constructor `ThemeStore(string root)`; `ThemeProfile Current {get;}`; event `EventHandler Changed` (worker-thread event, UI must marshal); `Save(ThemeProfile)` atomic/debounced as appropriate; `ImportBackground(string)` returning copied local path. Watch external theme changes and OS user preference changes. No UI timers when hidden.

`ThemeApplier.Apply(Window, ThemeProfile)` sets dynamic resources `Hub.Background` (alpha brush), `Hub.Surface`, `Hub.Foreground`, `Hub.Muted`, `Hub.Accent`, `Hub.Border`, `Hub.Glow`, `Hub.Image` (image brush or transparent), `Hub.Corner` (CornerRadius), `Hub.FontSize` (double). Applies window foreground, font, background as compatible, and root effect/glow through a helper if practical. Resolve System mode before applying. Callers build a layered image/background border using those resources. `ThemeApplier.ColorBrush(string)`.

`ModuleRuntime : IDisposable`: constructor `(string id, Action show, Action stop)`; `Start()`; current-user-only named pipe `ReFIXHub.<id>` scoped with current SID by a shared `ModuleRuntime.PipeName(id)` helper; line commands `status`, `show`, `stop`, bounded ASCII/UTF8 lines; `status` JSON includes `id`, `state=ON`, `pid`, `startedUtc` (process StartTime UTC ticks/string), `protocol=1`. state file `<Paths.State>/<id>.json` tracks process identity. Runtime deletes its own state on clean stop; host verifies PID/start time/path and uses pipe liveness for ON. Shared SDK documentation defines these details exactly as implemented.

## Host backend API contract

Namespace `ReFix.Hub`, src/Hub/Backend*.cs; compile against shared library.

`ModuleManifest`: public string properties `Id, Name, Description, Version, Kind` (process/fix), `EntryPoint, PackageUrl, Sha256, Preview` (mixer/notes/fix), plus `MinHubVersion`, `EnableArgument, DisableArgument, StatusArgument`; defaults documented. `CatalogDocument` has `SchemaVersion`, `List<ModuleManifest> Modules`.

`ModuleView : INotifyPropertyChanged`: `Manifest`, `State` string, `Detail` string, `InstalledVersion` string, `IsBusy` bool.

`ModuleManager : IDisposable`: constructor `(string dataRoot, string bundledCatalogPath)`; `IList<ModuleView> Modules {get;}`; `event EventHandler Changed` (UI marshal); `Task RefreshCatalogAsync()`; `Task InstallAsync(ModuleView)`; `Task StartAsync(ModuleView)`; `Task StopAsync(ModuleView)`; `Task ShowAsync(ModuleView)`; `Task RefreshStatesAsync()`; visible host calls refresh states every 2s only while visible. Reads bundled archives from `<base>/packages/<id>-<version>.zip` before downloading. Strict ID/path validation. `StopAsync` for fixes invokes disable; process modules receive stop; user-visible failures retain accurate state. Install/update refuses while running, cleans partial files, persists installed manifest. No autoapply after download. Bundle catalogue URI is fixed to this repo's raw main/catalog/catalog.json; because repository is currently empty, remote absence is an explanatory error with local catalogue retained. A developer release tool generates hashes/catalogue from actual build outputs.
