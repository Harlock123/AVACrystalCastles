namespace AVACrystalCastles.Engine;

/// <summary>
/// The computer at the stick, for the demonstration (and the tests). It jumps whatever is about to run
/// into it, runs from the bees, squashes grubs while they eat and anything at all while it wears the hat,
/// fetches the hat when things get close, grabs the honey, and otherwise walks to the nearest gem.
/// </summary>
public sealed class BearPilot
{
    public string Doing { get; private set; } = "";

    private bool _jumped;
    private (int X, int Y)? _flee;
    private double _fleeFor;

    public Controls Decide(GameEngine g)
    {
        var b = g.Bear;
        if (g.Phase != Phase.Play || !b.Alive) return default;
        var c = g.Castle;
        int i = (int)Math.Floor(b.X), j = (int)Math.Floor(b.Y);
        double bz = c.Z(b.X, b.Y);
        double Gap(double x, double y) => Math.Sqrt((x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y));
        // (Within a level up or down counts: on a stair the heights change quickly.)
        var threats = g.Enemies.Where(e => !(e.Kind == EnemyKind.Grub && e.Munch > 0) && Math.Abs(c.Z(e.X, e.Y) - bz) < 1.2).ToList();

        // Something about to run into it: jump.
        bool jump = false;
        if (b.Hat <= 0 && !b.Airborne && b.Lift <= 0 && threats.Any(e => Gap(e.X, e.Y) < 0.78))
        {
            jump = !_jumped;
            _jumped = jump;
        }
        else _jumped = false;

        (int X, int Y)? step = null;
        if (b.Hat > 0)
        {
            // Wearing the hat: go after the nearest enemy.
            Doing = "hunt";
            step = c.Toward(i, j, (x, y) => g.Enemies.Any(e => (int)e.X == x && (int)e.Y == y))?.Step;
        }
        if (step == null && g.Bees.Active && Gap(g.Bees.X, g.Bees.Y) < 4)
        {
            // Away from the bees, or to the hat if it's handy.
            Doing = "bees";
            var hat = g.Hat is { } h ? c.Toward(i, j, (x, y) => (x, y) == h) : null;
            if (hat is { Distance: < 6 } toHat) step = toHat.Step;
            else
            {
                // Once it has picked a way to run, it sticks to it a moment rather than dithering.
                if (_fleeFor <= 0 || _flee is not { } f || !c.CanStep(i, j, i + f.X, j + f.Y)) { _flee = Away(g, i, j, g.Bees.X, g.Bees.Y); _fleeFor = 0.5; }
                _fleeFor -= Field.Step;
                step = _flee;
            }
        }
        if (step == null)
        {
            var eating = c.Toward(i, j, (x, y) => g.Enemies.Any(e => e.Kind == EnemyKind.Grub && e.Munch > 0.3 && (int)e.X == x && (int)e.Y == y));
            if (eating is { Distance: < 4 }) { Doing = "squash"; step = eating.Value.Step; }
        }
        if (step == null && g.Hat is { } hat2 && threats.Any(e => Gap(e.X, e.Y) < 3))
        {
            var toHat = c.Toward(i, j, (x, y) => (x, y) == hat2);
            if (toHat is { Distance: < 8 }) { Doing = "hat"; step = toHat.Value.Step; }
        }
        if (step == null && g.Honey is { } honey)
        {
            var toHoney = c.Toward(i, j, (x, y) => (x, y) == honey);
            if (toHoney is { Distance: < 9 }) { Doing = "honey"; step = toHoney.Value.Step; }
        }
        if (step == null && c.Gems[i, j])
        {
            // A gem on this very square: to its middle to pick it up.
            Doing = "gems";
            var (hx, hy) = ScreenWay(i + 0.5 - b.X, j + 0.5 - b.Y);
            return new Controls(hx, hy, jump);
        }
        if (step == null)
        {
            // The nearest gem, keeping clear of squares with something on them.
            Doing = "gems";
            var near = threats.Where(e => Gap(e.X, e.Y) < 2.2).ToList();
            step = c.Toward(i, j, (x, y) => c.Gems[x, y] && !near.Any(e => (int)e.X == x && (int)e.Y == y))?.Step;
            if (step is { } s && near.Any(e => (int)e.X == i + s.X && (int)e.Y == j + s.Y) && !jump) step = Away(g, i, j, near[0].X, near[0].Y);
        }
        if (step is not { } st) return new Controls(0, 0, jump);
        // Aim at the next square's centre, keeping near the line between centres so corners don't snag.
        double tx = i + st.X + 0.5, ty = j + st.Y + 0.5;
        if (st.X != 0) ty = j + 0.5;
        if (st.Y != 0) tx = i + 0.5;
        var (dx, dy) = ScreenWay(tx - b.X, ty - b.Y);
        return new Controls(dx, dy, jump);
    }

    /// <summary>The open neighbouring square that puts the most distance between the bear and a point.</summary>
    private static (int X, int Y)? Away(GameEngine g, int i, int j, double fx, double fy)
    {
        (int, int)? best = null;
        double far = -1;
        foreach (var w in Castle.Ways)
        {
            if (!g.Castle.CanStep(i, j, i + w.X, j + w.Y)) continue;
            double d = Math.Abs(i + w.X + 0.5 - fx) + Math.Abs(j + w.Y + 0.5 - fy);
            if (d > far) { far = d; best = w; }
        }
        return best;
    }

    /// <summary>The stick (eight ways) that moves the bear along a way on the castle: the opposite of the engine's WorldWay.</summary>
    public static (int Dx, int Dy) ScreenWay(double wx, double wy)
    {
        double len = Math.Sqrt(wx * wx + wy * wy);
        if (len < 0.02) return (0, 0);
        // The nearest of the eight ways on the castle.
        double a = Math.Atan2(wy, wx);
        int octant = ((int)Math.Round(a / (Math.PI / 4)) + 8) % 8;
        (int X, int Y) w = octant switch { 0 => (1, 0), 1 => (1, 1), 2 => (0, 1), 3 => (-1, 1), 4 => (-1, 0), 5 => (-1, -1), 6 => (0, -1), _ => (1, -1) };
        // World (x, y) comes from the stick as x = dx + dy, y = dy − dx.
        int sdx = Math.Sign(w.X - w.Y), sdy = Math.Sign(w.X + w.Y);
        return (sdx, sdy);
    }
}
