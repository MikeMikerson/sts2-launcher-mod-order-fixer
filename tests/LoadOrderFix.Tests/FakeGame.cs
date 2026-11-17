using System.Collections.Generic;
using System.Linq;

namespace LoadOrderFix.Tests;

internal enum FakeState
{
    None,
    Loaded,
    Failed,
    Disabled,
}

internal sealed class FakeMod
{
    public FakeMod(string? id, params string?[] dependencies)
    {
        Id = id;
        Dependencies = dependencies;
    }

    public string? Id { get; }
    public IReadOnlyList<string?> Dependencies { get; }

    /// <summary>This mod is a copy of the fix: once loaded, it hooks every later load call.</summary>
    public bool IsFix { get; private init; }
    public FakeState State { get; set; }

    /// <summary>How many times the game reported this mod as detected (once per load call, like the game).</summary>
    public int TimesDetected { get; set; }

    public static FakeMod Fix(string id) => new(id) { IsFix = true };

    public override string ToString() => Id ?? "<no id>";
}

/// <summary>
/// The parts of the game's mod loading the fix depends on, as the game does them: a load call on a
/// mod that was already tried only reports it again, and a mod whose dependencies are not loaded
/// yet fails for good. <see cref="LauncherPass"/> tries the mods in folder order, as the Android
/// launcher does, instead of sorting them first.
/// </summary>
internal sealed class FakeGame
{
    private readonly List<DependencyFirstLoader<FakeMod>> _loaders = new();

    public FakeGame(IEnumerable<FakeMod> mods, bool withFix, params string[] disabledByPlayer)
        : this(mods, withFix ? 1 : 0, disabledByPlayer)
    {
    }

    /// <param name="fixCopies">How many copies of the fix are installed (each adds its own hook).</param>
    public FakeGame(IEnumerable<FakeMod> mods, int fixCopies, params string[] disabledByPlayer)
    {
        Mods = mods.ToList();
        DisabledByPlayer = new HashSet<string>(disabledByPlayer);
        for (int i = 0; i < fixCopies; i++)
        {
            AddFix();
        }
    }

    private void AddFix() => _loaders.Add(new DependencyFirstLoader<FakeMod>(
        allMods: () => Mods,
        describe: m => new ModInfo(m.Id, m.State == FakeState.None, m.State == FakeState.Loaded, m.Dependencies),
        tryLoad: TryLoadMod,
        isDisabledByPlayer: id => DisabledByPlayer.Contains(id),
        log: Log.Add));

    public List<FakeMod> Mods { get; }
    public HashSet<string> DisabledByPlayer { get; }
    public List<string> Log { get; } = new();

    public FakeMod this[string id] => Mods.First(m => m.Id == id);

    /// <summary>The Android launcher's pass: every mod, in folder order, no sorting.</summary>
    public void LauncherPass()
    {
        foreach (FakeMod mod in Mods.ToList())
        {
            TryLoadMod(mod);
        }
    }

    /// <summary>
    /// The game's <c>TryLoadMod</c>, with the fix's hooks in front when installed. As in Harmony, the
    /// hooks run in the order they were added, and the first one that returns false skips the
    /// remaining hooks and the original.
    /// </summary>
    public void TryLoadMod(FakeMod mod)
    {
        foreach (DependencyFirstLoader<FakeMod> loader in _loaders)
        {
            if (!loader.BeforeTryLoad(mod))
            {
                return;
            }
        }

        if (mod.State != FakeState.None)
        {
            mod.TimesDetected++;
            return;
        }

        if (mod.Id != null && DisabledByPlayer.Contains(mod.Id))
        {
            mod.State = FakeState.Disabled;
        }
        else
        {
            bool dependenciesLoaded = mod.Dependencies.All(
                id => Mods.Any(m => m.Id == id && m.State == FakeState.Loaded));
            mod.State = dependenciesLoaded ? FakeState.Loaded : FakeState.Failed;
        }
        mod.TimesDetected++;
        if (mod.IsFix && mod.State == FakeState.Loaded)
        {
            AddFix();
        }
    }
}
