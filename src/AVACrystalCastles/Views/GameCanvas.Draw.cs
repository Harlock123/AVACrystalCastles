using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVACrystalCastles.Engine;
using Effect = AVACrystalCastles.Engine.Effect;

namespace AVACrystalCastles.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVACrystalCastles/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Black = 0xFF101010;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    /// <summary>A point of the castle on screen.</summary>
    private static Point P(double x, double y, double z) => new(Field.OX + (x - y) * Field.TW / 2, Field.OY + (x + y) * Field.TH / 2 - z * Field.HZ);

    private const double Base = -0.7;

    /// <summary>Each castle's stone, and its walkway tops by height.</summary>
    private static readonly (uint Wall, uint Top)[] Stones =
    [
        (0xFF6A5AA0, 0xFF8A9AD8), (0xFF5A8A6A, 0xFF8AC89A), (0xFF8A5A5A, 0xFFC89A8A), (0xFF4A4A5A, 0xFF7A7A9A),
        (0xFF7A6A4A, 0xFFC0A878), (0xFF4A6A8A, 0xFF7AA8C8),
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF080A18), new Rect(0, 0, Field.Width, Field.Height));
        DrawSky(dc);
        DrawCastle(dc);
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    private static void DrawSky(DrawingContext dc)
    {
        for (int i = 0; i < 90; i++)
        {
            uint h = (uint)(i * 2654435761u);
            double x = h % 1024, y = Field.Top + (h >> 12) % 700;
            dc.FillRectangle(Brush(0x60C8D8FF), new Rect(x, y, 1.5, 1.5));
        }
    }

    private void DrawCastle(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Castle;
        var (wall, top) = Stones[(g.Level - 1) % Stones.Length];
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        // Everything that stands on the castle, by the diagonal row it is in, back to front.
        var things = new List<(double Key, Action Draw)>();
        if (inGame)
        {
            foreach (var e in g.Enemies) things.Add((Row(c, e.X, e.Y, c.Z(e.X, e.Y)) + (e.X + e.Y) * 1e-3, () => DrawEnemy(dc, e)));
            if (g.Bear.Alive || g.Phase == Phase.Dying)
                things.Add((Row(c, g.Bear.X, g.Bear.Y, c.Z(g.Bear.X, g.Bear.Y) + g.Bear.Lift) + (g.Bear.X + g.Bear.Y) * 1e-3, () => DrawBear(dc, g.Bear)));
            if (g.Bees.Active) things.Add((Row(c, g.Bees.X, g.Bees.Y, 99) + 0.9, () => DrawBees(dc, g.Bees)));
        }
        things.Sort((a, b) => a.Key.CompareTo(b.Key));
        int next = 0;
        for (int s = 0; s <= 2 * (Field.Size - 1); s++)
        {
            for (int i = Math.Max(0, s - Field.Size + 1); i <= Math.Min(s, Field.Size - 1); i++)
            {
                int j = s - i;
                var q = c.Squares[i, j];
                if (!q.Walk) continue;
                DrawSquare(dc, c, i, j, wall, top);
                double z = c.Z(i + 0.5, j + 0.5);
                if (!inGame) continue;
                if (c.Gems[i, j]) DrawGem(dc, P(i + 0.5, j + 0.5, z));
                if (g.Hat == (i, j)) DrawHatItem(dc, P(i + 0.5, j + 0.5, z));
                if (g.Honey == (i, j)) DrawHoney(dc, P(i + 0.5, j + 0.5, z));
            }
            // Things in this row, after its squares: those standing further forward are drawn by later rows.
            while (next < things.Count && things[next].Key < s + 1) things[next++].Draw();
        }
        while (next < things.Count) things[next++].Draw();
    }

    /// <summary>
    /// The diagonal row after which something standing at (x, y) at height z is drawn: the frontmost row of the
    /// squares its footprint reaches into, not counting any higher than it is (those are walls in front of it,
    /// and are drawn over it). Drawn any earlier, the floor of a square it is stepping into would be painted
    /// over its feet.
    /// </summary>
    private static int Row(Castle c, double x, double y, double z)
    {
        int best = (int)Math.Floor(x) + (int)Math.Floor(y);
        foreach (double dx in new[] { -0.35, 0.35 })
            foreach (double dy in new[] { -0.35, 0.35 })
            {
                int i = (int)Math.Floor(x + dx), j = (int)Math.Floor(y + dy);
                if (!Field.Inside(i, j)) continue;
                var q = c.Squares[i, j];
                if (q.Walk && q.H > z + 0.05) continue;
                best = Math.Max(best, i + j);
            }
        return best;
    }

    private static void DrawSquare(DrawingContext dc, Castle c, int i, int j, uint wall, uint top)
    {
        var q = c.Squares[i, j];
        double Corner(double x, double y) => c.Z(Math.Clamp(x, i + 0.0001, i + 0.9999), Math.Clamp(y, j + 0.0001, j + 0.9999));
        double z00 = Corner(i, j), z10 = Corner(i + 1, j), z11 = Corner(i + 1, j + 1), z01 = Corner(i, j + 1);
        uint topColour = Shade(top, 0.82 + 0.1 * q.H);
        // The two faces turned to the viewer, down to the base.
        Poly(dc, Shade(wall, 0.72), 0x40000000, 1, P(i + 1, j, z10), P(i + 1, j + 1, z11), P(i + 1, j + 1, Base), P(i + 1, j, Base));
        Poly(dc, Shade(wall, 0.95), 0x40000000, 1, P(i, j + 1, z01), P(i + 1, j + 1, z11), P(i + 1, j + 1, Base), P(i, j + 1, Base));
        Poly(dc, topColour, 0x30000000, 1, P(i, j, z00), P(i + 1, j, z10), P(i + 1, j + 1, z11), P(i, j + 1, z01));
        if (q.Stair)
        {
            // Steps across the slope.
            for (int k = 1; k < 4; k++)
            {
                double t = k / 4.0;
                (double, double) A, B;
                if (q.UpX != 0) { double x = q.UpX > 0 ? i + t : i + 1 - t; A = (x, j); B = (x, j + 1); }
                else { double y = q.UpY > 0 ? j + t : j + 1 - t; A = (i, y); B = (i + 1, y); }
                double za = Corner(A.Item1, A.Item2);
                dc.DrawLine(Pen(Shade(wall, 0.6), 2), P(A.Item1, A.Item2, za), P(B.Item1, B.Item2, za));
            }
        }
    }

    // ───────────────────────────── gems, the hat, honey ─────────────────────────────

    private void DrawGem(DrawingContext dc, Point p)
    {
        double tw = 0.6 + 0.4 * Math.Sin(_clock * 4 + p.X * 0.05);
        Poly(dc, 0xFF8AE8FF, 0xFF2A7AA0, 1, new Point(p.X, p.Y - 7), new Point(p.X + 4, p.Y - 3), new Point(p.X, p.Y + 1), new Point(p.X - 4, p.Y - 3));
        dc.FillRectangle(Brush((uint)(200 * tw) << 24 | 0x00FFFFFF), new Rect(p.X - 1, p.Y - 6, 2, 2));
    }

    private static void DrawHatItem(DrawingContext dc, Point p)
    {
        dc.DrawEllipse(Brush(0xFF6A2A9A), null, new Point(p.X, p.Y - 2), 13, 5);
        Poly(dc, 0xFF8A3AC0, 0xFF3A0A5A, 1.2, new Point(p.X - 8, p.Y - 3), new Point(p.X + 8, p.Y - 3), new Point(p.X + 3, p.Y - 26));
        dc.DrawEllipse(Brush(Yellow), null, new Point(p.X, p.Y - 13), 2.5, 2.5);
    }

    private static void DrawHoney(DrawingContext dc, Point p)
    {
        dc.DrawEllipse(Brush(0xFFE0A030), Pen(0xFF7A4A10, 1.5), new Point(p.X, p.Y - 10), 11, 10);
        dc.DrawRectangle(Brush(0xFFC07A20), null, new Rect(p.X - 8, p.Y - 22, 16, 5), 2, 2);
        Text(dc, "HONEY", new Point(p.X, p.Y - 9), 12, 0xFF5A2A00, center: true, shadow: false);
    }

    // ───────────────────────────── the bear and its enemies ─────────────────────────────

    private void DrawBear(DrawingContext dc, Bear b)
    {
        var g = Engine;
        double z = g.Castle.Z(b.X, b.Y);
        var ground = P(b.X, b.Y, z);
        var p = new Point(ground.X, ground.Y - b.Lift * Field.HZ * 1.6);
        if (b.Lift > 0) dc.DrawEllipse(Brush(0x60000000), null, ground, 11 - b.Lift * 2, 5);
        DrawBearAt(dc, p, b);
    }

    /// <summary>The bear with its feet at a point on screen.</summary>
    private void DrawBearAt(DrawingContext dc, Point p, Bear b, double scale = 1)
    {
        using var _ = dc.PushTransform(Matrix.CreateTranslation(-p.X, -p.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(p.X, p.Y));
        bool flash = b.Hat > 0 && b.Hat < 2 && (int)(_clock * 8) % 2 == 0;
        uint fur = !b.Alive ? 0xFF7A6A5A : 0xFFB0703A, dark = 0xFF6A3A1A;
        double step = Math.Sin(b.Walk * 9) * 3;
        dc.DrawEllipse(Brush(dark), null, new Point(p.X - 5 + step, p.Y - 2), 5, 3);
        dc.DrawEllipse(Brush(dark), null, new Point(p.X + 5 - step, p.Y - 2), 5, 3);
        dc.DrawEllipse(Brush(fur), Pen(dark, 1.2), new Point(p.X, p.Y - 13), 10, 11);
        dc.DrawEllipse(Brush(0xFFE0B080), null, new Point(p.X, p.Y - 11), 6, 7);
        var head = new Point(p.X + b.FaceX * 2, p.Y - 28);
        dc.DrawEllipse(Brush(fur), Pen(dark, 1), new Point(head.X - 7, head.Y - 7), 4, 4);
        dc.DrawEllipse(Brush(fur), Pen(dark, 1), new Point(head.X + 7, head.Y - 7), 4, 4);
        dc.DrawEllipse(Brush(fur), Pen(dark, 1.2), head, 9, 8);
        dc.DrawEllipse(Brush(0xFFE0B080), null, new Point(head.X + b.FaceX * 3, head.Y + 2), 4.5, 3.5);
        dc.DrawEllipse(Brush(Black), null, new Point(head.X + b.FaceX * 4, head.Y + 1), 1.8, 1.4);
        if (b.Alive)
        {
            dc.DrawEllipse(Brush(Black), null, new Point(head.X - 3 + b.FaceX * 2, head.Y - 2), 1.4, 1.6);
            dc.DrawEllipse(Brush(Black), null, new Point(head.X + 3 + b.FaceX * 2, head.Y - 2), 1.4, 1.6);
        }
        if (b.Hat > 0 && !flash)
        {
            Poly(dc, 0xFF8A3AC0, 0xFF3A0A5A, 1.2, new Point(head.X - 8, head.Y - 6), new Point(head.X + 8, head.Y - 6), new Point(head.X + 3, head.Y - 28));
            dc.DrawEllipse(Brush(Yellow), null, new Point(head.X + 1, head.Y - 16), 2.2, 2.2);
            dc.DrawEllipse(Brush(0x40B070FF), null, new Point(p.X, p.Y - 18), 22, 26);
        }
    }

    private void DrawEnemy(DrawingContext dc, Enemy e)
    {
        var g = Engine;
        var p = P(e.X, e.Y, g.Castle.Z(e.X, e.Y));
        switch (e.Kind)
        {
            case EnemyKind.Grub:
            {
                // A fat green caterpillar; chewing, it rears up.
                double chew = e.Munch > 0 ? Math.Abs(Math.Sin(_clock * 14)) * 4 : 0;
                for (int k = 3; k >= 0; k--)
                {
                    double off = Math.Sin(e.Age * 8 + k) * 1.5;
                    dc.DrawEllipse(Brush(k % 2 == 0 ? 0xFF6AC04A : 0xFF4AA03A), Pen(0xFF2A6A1A, 1), new Point(p.X - 8 + k * 5, p.Y - 6 - off - (k == 3 ? chew : 0)), 6, 6);
                }
                var hd = new Point(p.X + 9, p.Y - 10 - chew);
                dc.DrawEllipse(Brush(White), null, new Point(hd.X - 2, hd.Y - 3), 2.2, 2.6);
                dc.DrawEllipse(Brush(White), null, new Point(hd.X + 2, hd.Y - 3), 2.2, 2.6);
                dc.DrawEllipse(Brush(Black), null, new Point(hd.X - 2, hd.Y - 3), 1, 1.2);
                dc.DrawEllipse(Brush(Black), null, new Point(hd.X + 2, hd.Y - 3), 1, 1.2);
                if (e.Munch > 0) dc.DrawEllipse(Brush(0xFF6A1A1A), null, new Point(hd.X, hd.Y + 2), 3, 1 + chew * 0.4);
                break;
            }
            case EnemyKind.Orb:
            {
                var c = new Point(p.X, p.Y - 12);
                dc.DrawEllipse(Brush(0x40000000), null, new Point(p.X, p.Y), 11, 4);
                dc.DrawEllipse(Brush(0xD08AC8FF), Pen(0xFF3A6AA0, 1.5), c, 12, 12);
                double r = e.Age * 6;
                dc.DrawLine(Pen(0x80FFFFFF, 2), new Point(c.X + Math.Cos(r) * 10, c.Y + Math.Sin(r) * 4), new Point(c.X - Math.Cos(r) * 10, c.Y - Math.Sin(r) * 4));
                dc.DrawEllipse(Brush(0xE0FFFFFF), null, new Point(c.X - 4, c.Y - 5), 3, 2);
                break;
            }
            case EnemyKind.Tree:
            {
                double step = Math.Sin(e.Age * 6) * 3;
                dc.DrawLine(Pen(0xFF5A3A1A, 3), new Point(p.X - 3, p.Y - 10), new Point(p.X - 5 + step, p.Y));
                dc.DrawLine(Pen(0xFF5A3A1A, 3), new Point(p.X + 3, p.Y - 10), new Point(p.X + 5 - step, p.Y));
                dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(p.X - 4, p.Y - 26, 8, 18));
                foreach (var (ox, oy, r) in new[] { (0.0, -36.0, 12.0), (-8.0, -30.0, 9.0), (8.0, -30.0, 9.0) })
                    dc.DrawEllipse(Brush(0xFF2A7A3A), Pen(0xFF1A4A1A, 1), new Point(p.X + ox, p.Y + oy), r, r * 0.9);
                dc.DrawEllipse(Brush(Red), null, new Point(p.X - 3, p.Y - 20), 1.6, 1.6);
                dc.DrawEllipse(Brush(Red), null, new Point(p.X + 3, p.Y - 20), 1.6, 1.6);
                break;
            }
            case EnemyKind.Witch:
            {
                Poly(dc, 0xFF4A1A6A, 0xFF1A0A2A, 1.2, new Point(p.X - 11, p.Y), new Point(p.X + 11, p.Y), new Point(p.X + 3, p.Y - 26), new Point(p.X - 3, p.Y - 26));
                dc.DrawEllipse(Brush(0xFF7AC05A), Pen(0xFF2A5A1A, 1), new Point(p.X, p.Y - 31), 7, 7);
                dc.DrawEllipse(Brush(Red), null, new Point(p.X + 2, p.Y - 32), 1.5, 1.5);
                dc.DrawEllipse(Brush(0xFF2A1A3A), null, new Point(p.X, p.Y - 37), 12, 3);
                Poly(dc, 0xFF2A1A3A, null, 0, new Point(p.X - 6, p.Y - 37), new Point(p.X + 6, p.Y - 37), new Point(p.X - 2, p.Y - 56));
                dc.DrawLine(Pen(0xFF8A5A2A, 2.5), new Point(p.X - 16, p.Y - 8), new Point(p.X + 16, p.Y - 16));
                break;
            }
        }
    }

    private void DrawBees(DrawingContext dc, Swarm s)
    {
        var g = Engine;
        var p = P(s.X, s.Y, g.Castle.Z(Math.Clamp(s.X, 0, Field.Size - 0.01), Math.Clamp(s.Y, 0, Field.Size - 0.01)) + 1.4);
        for (int k = 0; k < 9; k++)
        {
            double a = _clock * 6 + k * 0.7, r = 6 + (k % 3) * 5;
            var b = new Point(p.X + Math.Cos(a) * r, p.Y + Math.Sin(a * 1.3) * r * 0.6);
            dc.DrawEllipse(Brush(Yellow), Pen(Black, 0.8), b, 3.2, 2.4);
            dc.DrawEllipse(Brush(0xA0FFFFFF), null, new Point(b.X, b.Y - 3), 2.5, 1.5);
        }
    }

    private static void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var p = P(e.X, e.Y, e.Z);
        switch (e.Kind)
        {
            case EffectKind.Sparkle:
                for (int k = 0; k < 4; k++)
                {
                    double a = k * Math.PI / 2 + t * 2, r = 4 + 12 * t;
                    dc.FillRectangle(Brush((uint)(255 * (1 - t)) << 24 | 0x00C8F8FF), new Rect(p.X + Math.Cos(a) * r - 1.5, p.Y - 6 + Math.Sin(a) * r * 0.6 - 1.5, 3, 3));
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(p.X, p.Y - t * 30), 24, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(p.X, p.Y - 20 - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0A0C1E), new Rect(0, 0, Field.Width, Field.Top));
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 34, White);
        Text(dc, "HI", new Point(170, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(170, 44), 28, Dim);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (!inGame) return;
        Text(dc, $"LEVEL {g.Level}", new Point(330, 16), 22, Dim);
        Text(dc, g.Castle.Name, new Point(330, 44), 26, Yellow);
        Text(dc, "GEMS", new Point(620, 16), 22, Dim);
        Text(dc, g.Castle.GemCount.ToString(), new Point(620, 44), 30, 0xFF8AE8FF);
        if (g.Bear.Hat > 0)
        {
            Text(dc, "HAT", new Point(710, 16), 22, 0xFFB070FF);
            dc.DrawRectangle(null, Pen(0xFF8A3AC0, 1.5), new Rect(710, 36, 90, 12));
            dc.FillRectangle(Brush(0xFFB070FF), new Rect(711, 37, 88 * g.Bear.Hat / GameEngine.HatSeconds, 10));
        }
        Text(dc, "BEARS", new Point(860, 16), 22, Dim);
        for (int k = 0; k < Math.Min(5, g.Lives - (g.Bear.Alive ? 1 : 0)); k++)
        {
            var c = new Point(872 + k * 28, 46);
            dc.DrawEllipse(Brush(0xFFB0703A), null, new Point(c.X - 6, c.Y - 6), 4, 4);
            dc.DrawEllipse(Brush(0xFFB0703A), null, new Point(c.X + 6, c.Y - 6), 4, 4);
            dc.DrawEllipse(Brush(0xFFB0703A), null, c, 8, 7);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xB0000000), new Rect(cx - 380, 92, 760, g.SubMessage != null ? 104 : 66));
        Text(dc, msg, new Point(cx, 122), 48, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, 168), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0080A18), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA CRYSTAL CASTLES", new Point(cx, 140), 90, 0xFF8AE8FF, center: true);
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 200), 28, Dim, center: true);
        string[] lines =
        [
            "GATHER EVERY GEM IN THE CASTLE BEFORE THE GRUBS EAT THEM",
            "JUMP WHAT RUNS AT YOU - SQUASH A GRUB WHILE IT EATS",
            "THE MAGIC HAT MAKES YOU UNSTOPPABLE FOR A WHILE",
            "TAKE THE LAST GEM YOURSELF FOR A BONUS",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 262 + i * 34), 26, White, center: true);
        (string Name, string Points)[] table =
        [
            ("GEM", "10"), ("GRUB, EATING", "500"), ("HONEY", "1000"), ("WITCH, WITH HAT", "5000"), ("LAST GEM", "500 x LEVEL"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            double x = 150 + i * 181;
            Text(dc, table[i].Name, new Point(x, 446), 22, Dim, center: true);
            Text(dc, table[i].Points, new Point(x, 472), 26, Yellow, center: true);
        }
        // The bear, with a few gems and the hat.
        DrawBearAt(dc, new Point(cx - 40, 590), new Bear { Walk = _clock * 0.5, FaceX = 1 }, 1.8);
        DrawGem(dc, new Point(cx + 70, 570));
        DrawGem(dc, new Point(cx + 110, 582));
        DrawGem(dc, new Point(cx + 150, 570));
        DrawHatItem(dc, new Point(cx - 160, 580));
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 640), 46, Yellow, center: true);
        Text(dc, "ARROWS (TWO FOR DIAGONALS): WALK    SPACE: JUMP", new Point(cx, 692), 26, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD8080A18), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TOP BEARS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD8080A18), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
