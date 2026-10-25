# Keyboard controls

All windows read `%LOCALAPPDATA%/ReFixHub/controls.json` through the shared
`ControlsStore` (or the root chosen by `REFIX_DATA_ROOT`). Changes are atomic
and watched across processes. A malformed, oversized, or conflicting external
file leaves the last valid configuration active.

| Action | Default | Scope |
| --- | --- | --- |
| Show / hide HUB | Ctrl+Alt+H | Global |
| Show / hide QuietMixer | Ctrl+Alt+V | Global while module runs |
| Show / hide Quick Note | Disabled | Global while module runs |
| Hide current window | Esc | Each window separately |
| Save note | Ctrl+S | Quick Note |

Global assignments require Ctrl or Alt. Reserved Windows combinations and F12
cannot be assigned. Empty text disables an action. Local shortcuts may repeat
in different windows; global shortcuts cannot shadow any local action. Shortcuts
use physical virtual keys, so keyboard layout changes do not change their
positions. The editor displays Latin/Russian key labels.

Arrows, Tab, Shift+Tab, Home, End and other standard slider/focus navigation
remain normal controls; these are not configurable actions.

`ControlsStore.Current` returns a copy. `Assign`, `Save` and `ResetDefaults`
validate before writing and throw when a changed global shortcut cannot be
reserved. `Changed` and `RuntimeChanged` run on a worker thread. UI consumers
must dispatch their own rendering updates to their UI thread.
Resetting shortcuts preserves the `StartMixerWithHub` startup preference.

Each process creates `GlobalKeyRegistration` on its owning window thread,
forwards its window messages to `ProcessMessage`, and disposes the registration
on that thread before destroying the window. Allocate two adjacent hotkey IDs
per registration. Configuration events post a private registered window message;
there is no idle polling or dependency on `Application.Current`.

A replacement is reserved before the old key is released. A late Windows
conflict keeps the actual old shortcut and reports the requested shortcut and
error separately. Runtime state is scoped by action, PID and process start time
in `controls-runtime`; dead or reused PIDs are not shown as active. Disposing a
registration releases its shortcut and removes its own runtime file. Managed
runtime changes trigger retries for blocked registrations; saving the same
profile also explicitly retries after an external application releases a key.

`ControlsTests.Run(Action<string,bool>)` covers parsing, conflicts, live config
reload, stale runtime identity, and actual Windows registration/rebind/disposal.
Native tests use only Ctrl+Alt+Shift+F22/F23 and isolated temporary data. Run in
an interactive Windows session outside the restricted sandbox; a sandbox may
refuse all native registrations.
