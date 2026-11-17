using Xunit;

namespace LoadOrderFix.Tests;

public class DependencyFirstLoaderTests
{
    // BaseLib v3.4.8 ships with a helper mod it depends on; the launcher installs both into one
    // folder, where BaseLib.json comes first.
    private static FakeMod[] BaseLibAsInstalled() => new[]
    {
        new FakeMod("BaseLib", "BaseLibCompatibilityHelper"),
        new FakeMod("BaseLibCompatibilityHelper"),
    };

    [Fact]
    public void Without_the_fix_a_mod_listed_before_its_dependency_fails()
    {
        var game = new FakeGame(BaseLibAsInstalled(), withFix: false);

        game.LauncherPass();

        Assert.Equal(FakeState.Failed, game["BaseLib"].State);
        Assert.Equal(FakeState.Loaded, game["BaseLibCompatibilityHelper"].State);
    }

    [Fact]
    public void With_the_fix_the_dependency_loads_first_and_both_load()
    {
        var game = new FakeGame(BaseLibAsInstalled(), withFix: true);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Loaded, m.State));
        Assert.Equal(new[] { "loading BaseLibCompatibilityHelper before BaseLib, which depends on it" }, game.Log);
    }

    [Fact]
    public void Every_mod_is_reported_to_the_game_exactly_once()
    {
        var game = new FakeGame(BaseLibAsInstalled(), withFix: true);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(1, m.TimesDetected));
    }

    [Fact]
    public void A_chain_in_reverse_order_loads_deepest_first()
    {
        var game = new FakeGame(
            new[] { new FakeMod("A", "B"), new FakeMod("B", "C"), new FakeMod("C") },
            withFix: true);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Loaded, m.State));
        Assert.All(game.Mods, m => Assert.Equal(1, m.TimesDetected));
        Assert.Equal(
            new[] { "loading B before A, which depends on it", "loading C before B, which depends on it" },
            game.Log);
    }

    [Fact]
    public void Mods_already_in_order_are_left_alone()
    {
        var game = new FakeGame(
            new[] { new FakeMod("BaseLibCompatibilityHelper"), new FakeMod("BaseLib", "BaseLibCompatibilityHelper") },
            withFix: true);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Loaded, m.State));
        Assert.Empty(game.Log);
    }

    [Fact]
    public void A_missing_dependency_still_fails_as_the_game_reports_it()
    {
        var game = new FakeGame(new[] { new FakeMod("A", "NotInstalled") }, withFix: true);

        game.LauncherPass();

        Assert.Equal(FakeState.Failed, game["A"].State);
        Assert.Empty(game.Log);
    }

    [Fact]
    public void A_dependency_cycle_fails_without_looping()
    {
        var game = new FakeGame(new[] { new FakeMod("A", "B"), new FakeMod("B", "A") }, withFix: true);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Failed, m.State));
        Assert.All(game.Mods, m => Assert.Equal(1, m.TimesDetected));
    }

    [Fact]
    public void A_dependency_the_player_turned_off_is_not_loaded_early()
    {
        var game = new FakeGame(new[] { new FakeMod("A", "B"), new FakeMod("B") }, withFix: true, "B");

        game.LauncherPass();

        Assert.Equal(FakeState.Failed, game["A"].State);
        Assert.Equal(FakeState.Disabled, game["B"].State);
        Assert.Empty(game.Log);
    }

    [Fact]
    public void A_dependency_that_already_failed_is_not_tried_again()
    {
        var game = new FakeGame(
            new[] { new FakeMod("B", "NotInstalled"), new FakeMod("A", "B") },
            withFix: true);

        game.LauncherPass();

        Assert.Equal(FakeState.Failed, game["A"].State);
        Assert.Equal(1, game["B"].TimesDetected);
        Assert.Empty(game.Log);
    }

    [Fact]
    public void Two_mods_sharing_a_dependency_load_it_once()
    {
        var game = new FakeGame(
            new[] { new FakeMod("A", "Lib"), new FakeMod("B", "Lib"), new FakeMod("Lib") },
            withFix: true);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Loaded, m.State));
        Assert.All(game.Mods, m => Assert.Equal(1, m.TimesDetected));
        Assert.Single(game.Log);
    }

    [Fact]
    public void Repeated_and_empty_dependency_ids_are_ignored()
    {
        var game = new FakeGame(
            new[] { new FakeMod("A", "B", "B", null, ""), new FakeMod("B") },
            withFix: true);

        game.LauncherPass();

        Assert.Equal(FakeState.Loaded, game["B"].State);
        Assert.Single(game.Log);
    }

    [Fact]
    public void With_two_copies_of_a_dependency_the_first_untried_one_loads()
    {
        var first = new FakeMod("Lib");
        var second = new FakeMod("Lib");
        var game = new FakeGame(new[] { new FakeMod("A", "Lib"), first, second }, withFix: true);

        game.TryLoadMod(game["A"]);

        Assert.Equal(FakeState.Loaded, game["A"].State);
        Assert.Equal(FakeState.Loaded, first.State);
        Assert.Equal(FakeState.None, second.State);
    }

    [Fact]
    public void Mods_without_an_id_do_not_break_loading()
    {
        var game = new FakeGame(new[] { new FakeMod(null), new FakeMod("A", "B"), new FakeMod("B") }, withFix: true);

        game.LauncherPass();

        Assert.Equal(FakeState.Loaded, game["A"].State);
        Assert.Equal(FakeState.Loaded, game["B"].State);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Several_copies_of_the_fix_still_load_every_mod_once(int copies)
    {
        var game = new FakeGame(
            new[] { new FakeMod("A", "B"), new FakeMod("B", "C"), new FakeMod("C") },
            copies);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Loaded, m.State));
        Assert.All(game.Mods, m => Assert.Equal(1, m.TimesDetected));
    }

    [Fact]
    public void Two_copies_of_the_fix_with_a_dependency_cycle_report_each_mod_once()
    {
        var game = new FakeGame(new[] { new FakeMod("A", "B"), new FakeMod("B", "A") }, fixCopies: 2);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Failed, m.State));
        Assert.All(game.Mods, m => Assert.Equal(1, m.TimesDetected));
    }

    [Fact]
    public void A_mod_tried_before_the_fix_loads_is_not_helped()
    {
        // "00Needy" sorts before "0LoadOrderFix", so it is tried before the fix is hooked in.
        var game = new FakeGame(
            new[] { new FakeMod("00Needy", "Lib"), FakeMod.Fix("0LoadOrderFix"), new FakeMod("Lib") },
            withFix: false);

        game.LauncherPass();

        Assert.Equal(FakeState.Failed, game["00Needy"].State);
        Assert.Equal(FakeState.Loaded, game["Lib"].State);
    }

    [Fact]
    public void A_mod_without_dependencies_sorting_before_the_fix_is_unaffected()
    {
        var game = new FakeGame(
            new[]
            {
                new FakeMod("00Other"),
                FakeMod.Fix("0LoadOrderFix"),
                new FakeMod("BaseLib", "BaseLibCompatibilityHelper"),
                new FakeMod("BaseLibCompatibilityHelper"),
            },
            withFix: false);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Loaded, m.State));
        Assert.All(game.Mods, m => Assert.Equal(1, m.TimesDetected));
    }

    [Fact]
    public void A_second_fix_named_to_sort_first_still_loads_every_mod_once()
    {
        var game = new FakeGame(
            new[]
            {
                FakeMod.Fix("00LoadOrderFix"),
                FakeMod.Fix("0LoadOrderFix"),
                new FakeMod("A", "B"),
                new FakeMod("B", "C"),
                new FakeMod("C"),
            },
            withFix: false);

        game.LauncherPass();

        Assert.All(game.Mods, m => Assert.Equal(FakeState.Loaded, m.State));
        Assert.All(game.Mods, m => Assert.Equal(1, m.TimesDetected));
    }
}
