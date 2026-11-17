using System;
using System.Collections.Generic;
using System.Linq;

namespace LoadOrderFix;

/// <summary>What the loader needs to know about one installed mod.</summary>
/// <param name="Id">The manifest id, or null when the manifest has none.</param>
/// <param name="Untried">The game has not tried to load this mod yet.</param>
/// <param name="Loaded">The game loaded this mod successfully.</param>
/// <param name="Dependencies">The ids this mod's manifest declares as dependencies.</param>
internal readonly record struct ModInfo(
    string? Id,
    bool Untried,
    bool Loaded,
    IReadOnlyList<string?> Dependencies);

/// <summary>
/// Makes every mod's dependencies load before the mod itself, whatever order the mods are tried in.
///
/// <see cref="BeforeTryLoad"/> runs before each call to the game's mod loader (<c>TryLoadMod</c>).
/// When the mod about to load declares a dependency that is installed but not tried yet, that
/// dependency is loaded first, through the same hooked loader, so its own dependencies come first
/// too. When the original caller later reaches a dependency that was loaded early, that call is
/// skipped, so the game sees every mod tried exactly once, as it would in a sorted load.
///
/// Anything this cannot help with (a missing, failed or player-disabled dependency, or a dependency
/// cycle) is left alone, and the game reports it as it always does.
///
/// It has no game types in it, so it can be tested without the game.
/// </summary>
/// <typeparam name="TMod">The game's mod type.</typeparam>
internal sealed class DependencyFirstLoader<TMod>
    where TMod : class
{
    private readonly Func<IReadOnlyList<TMod>> _allMods;
    private readonly Func<TMod, ModInfo> _describe;
    private readonly Action<TMod> _tryLoad;
    private readonly Func<string, bool> _isDisabledByPlayer;
    private readonly Action<string> _log;

    /// <summary>Mods whose dependencies are being loaded right now; breaks dependency cycles.</summary>
    private readonly HashSet<TMod> _inProgress = new(ReferenceEqualityComparer.Instance);

    /// <summary>Dependencies loaded ahead of their turn, whose own turn is still to come.</summary>
    private readonly HashSet<TMod> _loadedEarly = new(ReferenceEqualityComparer.Instance);

    /// <param name="allMods">Every installed mod the game knows about, in the game's order.</param>
    /// <param name="describe">Reads what the loader needs from one mod.</param>
    /// <param name="tryLoad">Calls the game's hooked mod loader, which runs <see cref="BeforeTryLoad"/> first.</param>
    /// <param name="isDisabledByPlayer">True when the player turned the mod with this id off.</param>
    /// <param name="log">Writes one line to the game's log.</param>
    public DependencyFirstLoader(
        Func<IReadOnlyList<TMod>> allMods,
        Func<TMod, ModInfo> describe,
        Action<TMod> tryLoad,
        Func<string, bool> isDisabledByPlayer,
        Action<string> log)
    {
        _allMods = allMods;
        _describe = describe;
        _tryLoad = tryLoad;
        _isDisabledByPlayer = isDisabledByPlayer;
        _log = log;
    }

    /// <summary>
    /// Runs before the game tries to load <paramref name="mod"/>.
    /// </summary>
    /// <returns>False when the game must skip this call, because the mod was already loaded early.</returns>
    public bool BeforeTryLoad(TMod mod)
    {
        if (_loadedEarly.Remove(mod))
        {
            return false;
        }

        LoadDependenciesFirst(mod);
        return true;
    }

    private void LoadDependenciesFirst(TMod mod)
    {
        ModInfo info = _describe(mod);
        if (!info.Untried || info.Dependencies.Count == 0 || !_inProgress.Add(mod))
        {
            return;
        }

        try
        {
            foreach (string? id in info.Dependencies.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                // Read again for every dependency: loading the previous one may have loaded this one.
                IReadOnlyList<TMod> all = _allMods();
                if (all.Any(m => _describe(m) is { Loaded: true } d && d.Id == id))
                {
                    continue;
                }

                TMod? waiting = all.FirstOrDefault(
                    m => !_inProgress.Contains(m) && _describe(m) is { Untried: true } d && d.Id == id);
                if (waiting == null || _isDisabledByPlayer(id))
                {
                    continue;
                }

                _log($"loading {id} before {info.Id}, which depends on it");
                _tryLoad(waiting);

                // Only skip its later turn if the game really tried it now.
                if (!_describe(waiting).Untried)
                {
                    _loadedEarly.Add(waiting);
                }
            }
        }
        finally
        {
            _inProgress.Remove(mod);
        }
    }
}
