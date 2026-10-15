using AVACrystalCastles.Engine;
using Engine = AVACrystalCastles.Engine;

namespace AVACrystalCastles.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avacrystalcastles-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play on a level, with no enemies, bees or honey unless a test puts them there.</summary>
    private static GameEngine Game(int level = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(level);
        g.ClearEnemies();
        g.NoGrace();
        return g;
    }

    /// <summary>The stick for a way along the castle (one square step), as the screen sees it.</summary>
    private static Engine.Controls Way(int wx, int wy, bool jump = false)
    {
        var (dx, dy) = BearPilot.ScreenWay(wx, wy);
        return new Engine.Controls(dx, dy, jump);
    }

    /// <summary>A walkable, flat square with a flat walkable neighbour the given way, and both at the same height.</summary>
    private static (int I, int J) FlatPair(Castle c, int wx, int wy) =>
        Castle.Cells().First(s => c.Squares[s.I, s.J].Walk && !c.Squares[s.I, s.J].Stair && c.CanStep(s.I, s.J, s.I + wx, s.J + wy) && !c.Squares[s.I + wx, s.J + wy].Stair);

    // ───────────────────────────── the castles ─────────────────────────────

    [Fact]
    public void Every_castle_has_gems_on_every_walkway_and_every_square_can_be_reached()
    {
        for (int level = 1; level <= 12; level++)
        {
            var c = Castle.Build(level);
            var reach = c.Reachable(c.Start);
            Assert.All(Castle.Cells(), s => Assert.Equal(c.Squares[s.I, s.J].Walk, reach[s.I, s.J]));
            Assert.True(c.GemCount > 80);
            Assert.All(Castle.Cells(), s => Assert.False(c.Gems[s.I, s.J] && (c.Squares[s.I, s.J].Stair || !c.Squares[s.I, s.J].Walk)));
            Assert.Contains(Castle.Cells(), s => c.Squares[s.I, s.J].H >= 2);
            Assert.Equal(Castle.Build(level).GemCount, c.GemCount);
        }
    }

    [Fact]
    public void Stairs_join_heights_and_walls_and_the_sides_of_stairs_cannot_be_crossed()
    {
        var c = Castle.Build(3);
        // (Some stairs lead only down to a dead end from their top; at least one joins heights properly.)
        var stair = Castle.Cells().First(s => c.Squares[s.I, s.J].Stair && Field.Inside(s.I - c.Squares[s.I, s.J].UpX, s.J - c.Squares[s.I, s.J].UpY)
                                             && c.CanStep(s.I - c.Squares[s.I, s.J].UpX, s.J - c.Squares[s.I, s.J].UpY, s.I, s.J));
        var q = c.Squares[stair.I, stair.J];
        Assert.Equal(q.H - 1, c.Squares[stair.I - q.UpX, stair.J - q.UpY].H);
        Assert.Equal(q.H - 1, c.Z(stair.I + 0.5 - q.UpX * 0.499, stair.J + 0.5 - q.UpY * 0.499), 1);
        Assert.Equal(q.H, c.Z(stair.I + 0.5 + q.UpX * 0.499, stair.J + 0.5 + q.UpY * 0.499), 1);
        // Never sideways.
        Assert.False(c.CanStep(stair.I, stair.J, stair.I + q.UpY, stair.J + q.UpX));
        // A wall: two flat squares a height apart can't be stepped between.
        var wall = Castle.Cells().SelectMany(s => Castle.Ways.Select(w => (s, w)))
            .First(p => Field.Inside(p.s.I + p.w.X, p.s.J + p.w.Y) && c.Squares[p.s.I, p.s.J].Walk && c.Squares[p.s.I + p.w.X, p.s.J + p.w.Y].Walk
                        && !c.Squares[p.s.I, p.s.J].Stair && !c.Squares[p.s.I + p.w.X, p.s.J + p.w.Y].Stair
                        && c.Squares[p.s.I, p.s.J].H != c.Squares[p.s.I + p.w.X, p.s.J + p.w.Y].H);
        Assert.False(c.CanStep(wall.s.I, wall.s.J, wall.s.I + wall.w.X, wall.s.J + wall.w.Y));
    }

    // ───────────────────────────── the bear ─────────────────────────────

    [Fact]
    public void The_bear_walks_the_way_the_stick_points_on_screen_and_picks_up_gems()
    {
        var g = Game();
        var (i, j) = FlatPair(g.Castle, 1, 0);
        g.Castle.Gems[i, j] = false;
        g.PutBear(i + 0.5, j + 0.5);
        g.Castle.Gems[i + 1, j] = true;
        int gems = g.Castle.GemCount;
        g.Run(25, Way(1, 0));
        Assert.Equal(i + 1, (int)Math.Floor(g.Bear.X));
        Assert.Equal(j, (int)Math.Floor(g.Bear.Y));
        Assert.False(g.Castle.Gems[i + 1, j]);
        Assert.Equal(gems - 1, g.Castle.GemCount);
        Assert.Equal(GameEngine.GemPoints, g.Score);
    }

    [Fact]
    public void A_wall_stops_the_bear()
    {
        var g = Game(3);
        var c = g.Castle;
        var (s, w) = Castle.Cells().SelectMany(s => Castle.Ways.Select(w => (s, w)))
            .First(p => Field.Inside(p.s.I + p.w.X, p.s.J + p.w.Y) && c.Squares[p.s.I, p.s.J].Walk && !c.Squares[p.s.I, p.s.J].Stair
                        && c.Squares[p.s.I + p.w.X, p.s.J + p.w.Y].Walk && !c.CanStep(p.s.I, p.s.J, p.s.I + p.w.X, p.s.J + p.w.Y));
        g.PutBear(s.I + 0.5, s.J + 0.5);
        g.Run(60, Way(w.X, w.Y));
        Assert.Equal(s.I, (int)Math.Floor(g.Bear.X));
        Assert.Equal(s.J, (int)Math.Floor(g.Bear.Y));
    }

    [Fact]
    public void The_bear_jumps_up_and_comes_down_again()
    {
        var g = Game();
        g.Run(1, new Engine.Controls(0, 0, true));
        double peak = 0;
        for (int t = 0; t < 60; t++) { g.Idle(1); peak = Math.Max(peak, g.Bear.Lift); }
        Assert.True(peak > 0.6);
        Assert.Equal(0, g.Bear.Lift);
    }

    // ───────────────────────────── enemies ─────────────────────────────

    [Fact]
    public void An_orb_rolls_after_the_bear_and_flattens_it()
    {
        var g = Game();
        var (i, j) = g.Castle.Start;
        var spot = Castle.Cells().First(s => g.Castle.Squares[s.I, s.J].Walk && !g.Castle.Squares[s.I, s.J].Stair && Math.Abs(s.I - i) + Math.Abs(s.J - j) == 5 && g.Castle.Squares[s.I, s.J].H == 0);
        g.PutEnemy(EnemyKind.Orb, spot.I, spot.J);
        for (int t = 0; t < 60 * 10 && g.Phase == Phase.Play; t++) g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("FLATTENED BY AN ORB", g.Cause);
    }

    [Fact]
    public void A_jump_carries_the_bear_over_an_orb()
    {
        var g = Game();
        var (i, j) = FlatPair(g.Castle, 1, 0);
        g.PutBear(i + 0.5, j + 0.5);
        var orb = g.PutEnemy(EnemyKind.Orb, i + 1, j);
        orb.X = i + 1.15;
        g.Run(1, new Engine.Controls(0, 0, true));
        g.Idle(18);
        Assert.Equal(Phase.Play, g.Phase);
    }

    [Fact]
    public void A_grub_eats_gems_and_can_be_squashed_only_while_it_eats()
    {
        var g = Game();
        var (i, j) = FlatPair(g.Castle, 1, 0);
        g.Castle.Gems[i + 1, j] = true;
        var grub = g.PutEnemy(EnemyKind.Grub, i + 1, j);
        g.Idle(2);
        Assert.True(grub.Munch > 0);
        g.PutBear(i + 1.5, j + 0.5);
        g.Idle(1);
        Assert.True(grub.Dead || !g.Enemies.Contains(grub));
        Assert.Equal(grub.Points, g.Score - (g.Castle.Gems[i + 1, j] ? 0 : GameEngine.GemPoints));
        // Not eating, it catches the bear.
        var g2 = Game();
        var (a, b) = FlatPair(g2.Castle, 1, 0);
        g2.Castle.Gems[a + 1, b] = false;
        g2.PutEnemy(EnemyKind.Grub, a + 1, b).Age = 0;
        g2.PutBear(a + 1.5, b + 0.5);
        g2.Idle(1);
        Assert.Equal(Phase.Dying, g2.Phase);
    }

    [Fact]
    public void A_grub_left_alone_swallows_the_gem()
    {
        var g = Game();
        var (i, j) = FlatPair(g.Castle, 1, 0);
        g.Castle.Gems[i + 1, j] = true;
        g.PutEnemy(EnemyKind.Grub, i + 1, j);
        g.PutBear(g.Castle.Start.I + 0.5, g.Castle.Start.J + 0.5);
        g.Idle((int)(60 * (GameEngine.MunchSeconds + 0.3)));
        Assert.False(g.Castle.Gems[i + 1, j]);
    }

    [Fact]
    public void The_magic_hat_lets_the_bear_squash_anything_for_a_while()
    {
        var g = Game();
        var (i, j) = FlatPair(g.Castle, 1, 0);
        g.SetHat((i + 1, j));
        g.PutBear(i + 0.5, j + 0.5);
        g.Run(25, Way(1, 0));
        Assert.True(g.Bear.Hat > 0);
        var orb = g.PutEnemy(EnemyKind.Orb, (int)Math.Floor(g.Bear.X), (int)Math.Floor(g.Bear.Y));
        g.Idle(1);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(orb.Dead || !g.Enemies.Contains(orb));
        g.Idle((int)(60 * GameEngine.HatSeconds));
        Assert.Equal(0, g.Bear.Hat);
    }

    [Fact]
    public void The_bees_come_out_and_chase_and_a_jump_is_no_use_against_them()
    {
        var g = Game();
        g.Calm = false;
        g.SetLevelTime(GameEngine.BeesAt - 0.05);
        g.Idle(5);
        Assert.True(g.Bees.Active);
        for (int t = 0; t < 60 * 12 && g.Phase == Phase.Play; t++) g.Run(1, new Engine.Controls(0, 0, t % 40 == 0));
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("STUNG BY THE BEES", g.Cause);
    }

    [Fact]
    public void Honey_pays_a_thousand()
    {
        var g = Game();
        var (i, j) = FlatPair(g.Castle, 1, 0);
        g.Castle.Gems[i + 1, j] = false;
        g.Castle.Gems[i, j] = false;
        g.SetHoney((i + 1, j));
        g.PutBear(i + 0.5, j + 0.5);
        g.Run(25, Way(1, 0));
        Assert.Null(g.Honey);
        Assert.Equal(GameEngine.HoneyPoints, g.Score);
    }

    // ───────────────────────────── levels and bears ─────────────────────────────

    [Fact]
    public void Taking_the_last_gem_clears_the_castle_with_the_last_gem_bonus()
    {
        var g = Game(2);
        foreach (var s in Castle.Cells()) g.Castle.Gems[s.I, s.J] = false;
        var (i, j) = FlatPair(g.Castle, 1, 0);
        g.Castle.Gems[i + 1, j] = true;
        g.PutBear(i + 0.5, j + 0.5);
        g.Run(25, Way(1, 0));
        Assert.Equal(Phase.LevelClear, g.Phase);
        Assert.Equal(GameEngine.GemPoints + 2000 + 1000, g.Score);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 2);
        Assert.Equal(3, g.Level);
    }

    [Fact]
    public void The_witch_comes_to_every_fourth_castle()
    {
        foreach (int level in new[] { 1, 2, 3, 4, 8 })
        {
            var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(level);
            Assert.Equal(level % 4 == 0, g.Enemies.Any(e => e.Kind == EnemyKind.Witch));
            Assert.Contains(g.Enemies, e => e.Kind == EnemyKind.Grub);
            Assert.Contains(g.Enemies, e => e.Kind == EnemyKind.Orb);
        }
    }

    [Fact]
    public void A_lost_bear_starts_again_and_the_last_ends_the_game()
    {
        var g = Game();
        var (i, j) = FlatPair(g.Castle, 1, 0);
        g.PutBear(i + 0.5, j + 0.5);
        g.PutEnemy(EnemyKind.Tree, i, j);
        g.Idle(2);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal(g.Castle.Start.I + 0.5, g.Bear.X);
        g.SetLives(1);
        g.PutEnemy(EnemyKind.Tree, g.Castle.Start.I, g.Castle.Start.J);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 1)));
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_bear_clears_the_first_castle()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 150 && g.Level == 1 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(2, g.Level);
    }

    [Fact]
    public void Left_alone_the_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Bear.X:0.000} {g.Bear.Y:0.000} {g.Level} {g.Lives} {g.Castle.GemCount}";
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetScore(99999);
        g.SetLives(1);
        g.PutEnemy(EnemyKind.Tree, g.Castle.Start.I, g.Castle.Start.J);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "GEM") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("GEM", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
