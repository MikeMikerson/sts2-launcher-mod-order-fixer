# Load Order Fix

A small Slay the Spire 2 mod for the Android StS2 Launcher. It fixes mods that show red with
"WITH ERRORS" because the launcher loaded them before a mod they need.

## The problem

Some mods need another mod to work. For example, BaseLib needs a small helper mod called
BaseLib Compatibility Helper, and the helper has to load first.

On PC, the game takes care of this: before loading anything, it sorts your mods so every mod's
helpers load first.

The Android launcher loads mods a different way. It skips that sorting step and loads them in
alphabetical order by name. If a mod's name comes before its helper's name, the game tries the
mod first, the helper is not loaded yet, and the mod fails. The game never tries it again, even
though everything it needs is installed. That is why a mod can fail on Android when the same files
work fine on PC. The game's log shows it as:

```
Tried to load mod BaseLib, but it depends on mods which have not been loaded: BaseLibCompatibilityHelper!
```

## What this mod does

It puts the sorting step back. Its name starts with "0", so it loads before your other mods. From
then on, whenever the game is about to load a mod, it first loads any helpers that mod needs.
Everything loads in the right order, whatever the names are.

On PC it does nothing, because the game already sorts mods there.

## Install

1. Download `0LoadOrderFix-v<version>.zip` from the [Releases](../../releases) page.
2. In the launcher, add a mod and pick that zip.
3. Install the other mods you want as usual, including the mods they depend on.
4. Fully close the game and start it again.

The main menu should now say "Running Modded. Loaded N mods." with no "WITH ERRORS". In the
game's log you will see a line like:

```
[0LoadOrderFix] loading BaseLibCompatibilityHelper before BaseLib, which depends on it
```

Keep its folder name starting with "0": it can only help mods that load after it.

## Tested with

- Android StS2 Launcher 0.3.17 on a Pixel 6a, Slay the Spire 2 v0.113.1, BaseLib v3.4.8
  (installed from its release zip, so BaseLib and its helper share one folder). Without this mod
  BaseLib failed; with it, all three mods loaded.

## What it cannot fix

- A mod built for an older version of the game. Look for an updated version of that mod.
- A missing helper. You still need to install every mod the one you want depends on.
- A mod that needs another mod but does not list it under `dependencies` in its `.json` file.
  This mod only knows about dependencies a mod declares.

## Safety

The whole mod is two short files in [`src/LoadOrderFix`](src/LoadOrderFix), and you can read all
of it. It:

- changes only the order mods are loaded in, by hooking one method of the game's mod loader
  (`ModManager.TryLoadMod`) with Harmony, which the game already includes;
- only loads mods you already installed, and never one you turned off in the game's mod settings;
- does not use the network, does not read or write any files, and does not touch your saves;
- does not change gameplay (`"affects_gameplay": false`), so it does not affect multiplayer
  compatibility checks.

If a game update changes the mod loader it hooks, it writes a warning to the log and does nothing,
so your mods load exactly as they would without it.

Each release lists the zip's SHA-256. To check your download matches:

```sh
shasum -a 256 0LoadOrderFix-v<version>.zip     # macOS and Linux
certutil -hashfile 0LoadOrderFix-v<version>.zip SHA256     # Windows
```

The build is reproducible: building the same release tag yourself gives the same DLL.

## Build from source

You need the .NET 9 SDK (or newer) and your own copy of Slay the Spire 2; the game's files are
never part of this repository.

```sh
./package.sh                                   # macOS, game in the default Steam folder
./package.sh -p:Sts2DataPath=<folder with sts2.dll>    # any other location
```

On Windows the folder is `<game folder>/data_sts2_windows_x86_64`. The script checks the versions
match, runs the tests, builds the mod and writes the zip and its SHA-256 to `dist/`.

## Tests

The loading logic ([`DependencyFirstLoader.cs`](src/LoadOrderFix/DependencyFirstLoader.cs)) has no
game types in it. The tests run it against a stand-in for the game's mod loader and the launcher's
loading order, so they run anywhere without the game, including on every push through GitHub
Actions:

```sh
dotnet test tests/LoadOrderFix.Tests
```

## License

[MIT](LICENSE). This is a fan-made mod, not affiliated with or endorsed by Mega Crit.
