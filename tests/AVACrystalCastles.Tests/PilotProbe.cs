using AVACrystalCastles.Engine;

namespace AVACrystalCastles.Tests;

/// <summary>How the computer bear gets on, castle by castle (set CASTLES_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        if (Environment.GetEnvironmentVariable("CASTLES_PROBE") is null) return;
        for (int level = 1; level <= 8; level++)
        {
            var c = Castle.Build(level);
            output.WriteLine($"castle {level} {c.Name}: {c.GemCount} gems, {Castle.Cells().Count(x => c.Squares[x.I, x.J].Stair)} stairs, heights {string.Join("/", Enumerable.Range(0, 4).Select(h => Castle.Cells().Count(x => c.Squares[x.I, x.J].Walk && c.Squares[x.I, x.J].H == h)))}");
        }
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "castles-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var levels = new List<string>();
            var last = g.Phase;
            int level = 1, t = 0;
            double start = 0;
            for (; t < 60 * 600 && g.Level <= 5; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"L{g.Level} {g.Cause}"] = deaths.GetValueOrDefault($"L{g.Level} {g.Cause}") + 1;
                if (g.Level != level) { levels.Add($"L{level}:{(t - start) / 60:0}s"); level = g.Level; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: level {g.Level} after {t / 60:0}s score {g.Score} gems left {g.Castle.GemCount} | {string.Join(" ", levels)} | doing {g.Pilot.Doing}");
            output.WriteLine("   " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
        }
    }
}
