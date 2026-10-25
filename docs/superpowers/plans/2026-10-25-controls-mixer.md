# Mixer / controls implementation plan

**Goal:** Complete mixer migration/distribution and editable key assignments without idle polling.
**Spec:** ../specs/2026-10-25-controls-mixer.md
**Execution:** isolated Shared controls, migration/release tasks; root integrates UI/host/modules; final independent review and full verification.

- [x] Shared key model/store/native registrations: conflict and rebind RED/GREEN tests.
- [x] Version 1.1 package/update/release tooling and safe legacy migration.
- [x] Modeless ControlsWindow, keyboard map, host and module dynamic key handlers.
- [x] Test real mixer and note IPC, assigned keys/theme preservation, cold tray and UI rendering.
- [x] Commit locally, publish authorized source/release if credentials allow; report concrete authentication or CI blockers.
