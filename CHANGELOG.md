# Changelog

## v0.1.0 (2026-11-18)

- First version: loads each mod's dependencies before the mod itself, so mods no longer fail with
  "depends on mods which have not been loaded" in the Android StS2 Launcher.
- Every mod is still reported to the game once, and missing, failed, turned-off and circular
  dependencies are left to the game to report as before.
- Does nothing if a game update changes the mod loader it hooks.
