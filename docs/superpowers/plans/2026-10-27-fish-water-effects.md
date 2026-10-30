# Fish water effects implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task in the current workspace.

**Goal:** Integrate swimming bubbles/wakes, takeoff spray, and reentry crown/foam particles into the playable fishing scene.

**Architecture:** A disposable fish-water presenter reads confirmed fish observations and samples four shared, bounded ParticleSystem buffers. Persistent visual settings reference the shader so builds retain it. World-space one-shot footprints stay at the actual crossing location; fish interruption never schedules a future landing.

**Tech Stack:** Unity 2022.3 built-in renderer, C#, shader, EditMode integration checks.

**Spec:** User-approved concept images in `artifacts/fish-water-vfx-concepts/2026-10-27/README.md` and the current request to implement and integrate all three stages.

## Global constraints

- Keep the existing 2× fish display scale and all simulation/network rules unchanged.
- Only confirmed state triggers water effects; presentation never writes authority.
- No preview or automated play component enters the production scene/build.
- Work in the existing Unity project; preserve unrelated working changes. No commit requested.
- Compile and visually inspect the production scene; do not run full transport/storage verification for rendering-only changes.

## Review focus

- Repeated confirmed frames must not add particles or restart bursts.
- Joining/restoring midair must not replay historical takeoff.
- Hooking midway must not create an anticipated landing splash.
- Restart, generation replacement and backwards time must clear old effects.
- Foam must remain fixed at the crossing point as the fish moves forward.

### Task 1: Observe confirmed motion and render the three particle stages

**Files:** New `Runtime/Presentation/Water/FishWaterEffects.cs`, `Runtime/Presentation/Water/FishWaterParticles.cs`, `Runtime/Data/Visuals/FishWaterEffectsSettings.cs`, `Shaders/FishWaterParticle.shader`, `Resources/FishWaterEffects.asset`; modify `Runtime/Presentation/FishingPresentation.cs`, `Runtime/Presentation/Fish/CatchEscapePresentation.cs`; new `Tests/Editor/FishWaterEffectsTests.cs` (all under `samples/TacticalGame.Unity/Assets/HookIsland`).

**Interfaces:** `FishWaterEffects(Transform parent, IslandMap map, FishVisualCatalog art)`; `Present(HookState confirmed)`, `Clear()`, `Dispose()`. Existing `FishingPresentation.Present` remains unchanged. Escape presentation gains an optional switch to leave splash drawing to the new presenter while retaining the existing pose.

- [x] Add integration checks through the existing presentation API: actual particles during swimming and crossings; same-tick stability; no historical or interrupted landing burst; fixed footprints; clear/reset; unchanged encoded state.
- [x] Run the new tests and verify failure because there are no particle effects yet.
- [x] Implement four pooled particle layers (bubbles, broken foam arcs, mesh droplets, mesh crown) and bounded confirmed-observation transitions. Add configurable tint, counts, lifetime, water height and gap limits.
- [x] Replace old normal-fish line rings, integrate escape reentry, and release/clear all owned resources.
- [x] Compile and run the focused new tests plus existing fish/escape integration tests.

### Task 2: Verify the actual scene and document tuning

**Files:** `docs/fish-water-effects-implementation.md`; evidence under ignored `artifacts/fish-water-vfx/`.

- [x] Run `HookPlatform.unity` as a local host and inspect swimming/takeoff/reentry particle activity and console.
- [x] Render closeups and the real playable scene, inspect actual images, adjust density/size if needed.
- [x] Record configuration, confirmed-state boundaries and validation results. Stop any play session started for verification and leave the formal scene clean.

## Execution notes

Implementation proceeds immediately under the user's request. Worktree/approval handoffs, unrelated full-suite runs and automatic commits are omitted to follow the current project and session instructions. Tests target event continuity and state immutability; appearance is checked by rendering rather than implementation-mirroring assertions.

Validation completed: 8/8 focused EditMode checks; formal local UDP host with all four particle layers active; real renders inspected and saved; read-only review accepted. No authority, protocol, storage or scene asset changes.
