using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVACrystalCastles.Engine;
using AVACrystalCastles.Views;

namespace AVACrystalCastles.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set CASTLES_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avacrystalcastles-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("CASTLES_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVACrystalCastles-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer bear play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static double Gap(GameEngine g, Enemy e) => Math.Abs(e.X - g.Bear.X) + Math.Abs(e.Y - g.Bear.Y);

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 71250, 7);
        g.HighScores.Insert("AVA", 52800, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Through_the_castles()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(30);
        g.Idle(40);
        c.Advance(0.5);
        Shot(w, c, "level-start");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.LevelTime > 18 && g.Enemies.Any(e => Gap(g, e) < 4)));
        Shot(w, c, "gathering");
        Assert.True(PlayUntil(g, c, g => g.Bear.Airborne && g.Bear.Lift > 0.6));
        Shot(w, c, "jump");
        Assert.True(PlayUntil(g, c, g => g.Enemies.Any(e => e.Kind == EnemyKind.Grub && e.Munch > 0.3 && Gap(g, e) < 6)));
        Shot(w, c, "grub");
        Assert.True(PlayUntil(g, c, g => g.Bees.Active && Math.Abs(g.Bees.X - g.Bear.X) + Math.Abs(g.Bees.Y - g.Bear.Y) < 4));
        Shot(w, c, "bees");
        Assert.True(PlayUntil(g, c, g => g.Bear.Hat > GameEngine.HatSeconds - 2, 300));
        Shot(w, c, "magic-hat");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.LevelClear && g.PhaseTime > 0.4, 300));
        Shot(w, c, "level-clear");
        g.Go(4);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.LevelTime > 10 && g.Enemies.Any(e => e.Kind == EnemyKind.Witch && Gap(g, e) < 6)));
        Shot(w, c, "witch");
        g.Go(6);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.LevelTime > 25 && g.Castle.GemCount < 120));
        Shot(w, c, "twin-spires");
    }
}
