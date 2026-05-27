# Join-point reload cache crash notes

This note documents the reload-cache failures seen while testing RimWorld 1.6 join-point creation.

## Observed failures

- Host-side world generation failed during join-point creation with a `WorldGenStep_Roads` key error involving `RimWorld.SurfaceLayer`.
- After changing the prototype to avoid world-grid reuse, the host still threw repeated map rendering errors after a client joined:
  - `MapDrawLayer_ExteriorLightingOverlay.get_Visible`
  - `SectionLayer_Darkness.get_Visible`
  - `SectionLayer_LightingOverlay.Regenerate`

## Cause

The old reload cache copied whole owner object graphs across `MemoryUtility.ClearAllMapsAndWorld()` and a save/load boundary.

That is unsafe in RimWorld 1.6 because these objects now own load references, native arrays, draw layers, and map-local state:

- `WorldGrid`
- `PlanetLayer`
- `SurfaceLayer`
- `MapDrawer`
- `Section`
- `SectionLayer`
- `WorldDrawLayer`

Reusing them after vanilla had created a fresh world or map could leave stale layer identity, stale map references, and stale native/render-owned state.

## Prototype decision

The prototype removes cache copying for `WorldGrid` and `MapDrawer` and lets vanilla rebuild those objects from saved data.

`SaveLoad.SaveAndReload` still restores transient multiplayer and UI state that is not naturally preserved by the reload:

- pawn tween positions
- local player faction
- map and global command queues
- chat window
- selected objects
- world render mode
- music manager

The expected tradeoff is slower join-point creation, but with fresh vanilla-owned world and map objects after reload.
