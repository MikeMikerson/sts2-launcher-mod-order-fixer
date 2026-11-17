using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace LoadOrderFix;

/// <summary>
/// Connects <see cref="DependencyFirstLoader{TMod}"/> to the game.
///
/// The game sorts mods so every dependency loads before the mods that need it. The Android StS2
/// Launcher loads the mods in its own Mods folder in a second pass that skips that sort, so a mod
/// that comes first alphabetically fails with "depends on mods which have not been loaded", even
/// when its dependency is installed right next to it. This hooks the game's private
/// <c>ModManager.TryLoadMod</c> so dependencies load first.
///
/// It only affects mods tried after it loads, which is why its id starts with "0". Where the game
/// has already sorted the mods (desktop), every dependency is loaded by then and it does nothing.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    public const string ModId = "0LoadOrderFix";
    private const string LogPrefix = "[" + ModId + "] ";

    private static DependencyFirstLoader<Mod>? _loader;

    public static void Initialize()
    {
        MethodInfo? tryLoadMod = AccessTools.Method(typeof(ModManager), "TryLoadMod", new[] { typeof(Mod) });
        FieldInfo? mods = AccessTools.Field(typeof(ModManager), "_mods");
        FieldInfo? settings = AccessTools.Field(typeof(ModManager), "_settings");
        if (tryLoadMod is not { IsStatic: true } || tryLoadMod.ReturnType != typeof(void)
            || mods is not { IsStatic: true } || mods.FieldType != typeof(List<Mod>))
        {
            // A game update changed the mod loader. Load mods exactly as before rather than guess.
            Log.Warn(LogPrefix + "this game version's mod loader is not the one this mod knows; doing nothing");
            return;
        }

        _loader = new DependencyFirstLoader<Mod>(
            allMods: () => (List<Mod>?)mods.GetValue(null) ?? new List<Mod>(),
            describe: Describe,
            tryLoad: mod => tryLoadMod.Invoke(null, new object[] { mod }),
            isDisabledByPlayer: id => IsDisabledByPlayer(settings, id),
            log: message => Log.Info(LogPrefix + message));

        try
        {
            new Harmony(ModId).Patch(
                tryLoadMod,
                prefix: new HarmonyMethod(typeof(MainFile), nameof(BeforeTryLoadMod)));
        }
        catch (Exception e)
        {
            _loader = null;
            Log.Warn(LogPrefix + "could not hook the mod loader; doing nothing: " + e);
            return;
        }

        Log.Info(LogPrefix + "ready: mods loaded after this one get their dependencies loaded first");
    }

    /// <summary>Harmony prefix of <c>ModManager.TryLoadMod(Mod)</c>; false skips the original.</summary>
    /// <param name="__0">The mod about to load (by position, so a renamed parameter does not matter).</param>
    private static bool BeforeTryLoadMod(Mod __0)
    {
        try
        {
            return _loader?.BeforeTryLoad(__0) ?? true;
        }
        catch (Exception e)
        {
            // Never break loading: the game goes on and reports any missing dependency itself.
            Log.Warn(LogPrefix + "could not load the dependencies of " + __0?.manifest?.id + " first: " + e);
            return true;
        }
    }

    private static ModInfo Describe(Mod mod) => new(
        mod.manifest?.id,
        Untried: mod.state == ModLoadState.None,
        Loaded: mod.state == ModLoadState.Loaded,
        Dependencies: mod.manifest?.dependencies?.Select(d => d?.id).ToList() ?? new List<string?>());

    /// <summary>True when the player turned this mod off in the game's mod settings.</summary>
    private static bool IsDisabledByPlayer(FieldInfo? settingsField, string id)
    {
        var settings = settingsField?.GetValue(null) as ModSettings;
        SettingsSaveMod? entry = settings?.ModList?.FirstOrDefault(s => s.Id == id);
        return entry is { IsEnabled: false };
    }
}
