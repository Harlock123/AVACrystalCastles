namespace AVACrystalCastles.Engine;

public sealed partial class GameEngine
{
    public const double BearSpeed = 3.2, JumpSpeed = 5.2, Gravity = 19;
    public const double StartSeconds = 2.4, DyingSeconds = 2.4, ClearSeconds = 3.4;
    public const double HatSeconds = 8, HoneyAt = 20, HoneySeconds = 12, BeesAt = 32, BeesSeconds = 12, BeeSpeed = 2.3;
    public const double MunchSeconds = 0.8, TouchRange = 0.48;
    public const int GemPoints = 10, HoneyPoints = 1000, BeePoints = 3000;
    public const int FirstExtraLife = 25000, ExtraLifeEvery = 50000;

    public int Level { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Castle Castle { get; private set; } = Castle.Build(1);
    public Bear Bear { get; } = new();
    public List<Enemy> Enemies { get; } = [];
    public Swarm Bees { get; } = new();
    /// <summary>Where the magic hat is waiting (if it hasn't been taken), and the honey pot (while it's out).</summary>
    public (int I, int J)? Hat { get; private set; }
    public (int I, int J)? Honey { get; private set; }
    public BearPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double LevelTime { get; private set; }
    /// <summary>For tests: no enemies, bees or honey unless a test puts them there.</summary>
    internal bool Calm { get; set; }

    public bool WitchLevel => Level % 4 == 0;
    public double Pace => Math.Min(1.5, 1 + 0.05 * (Level - 1));

    private int _nextExtra;
    private bool _jumpPrev, _honeyDone;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Level = 1;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.LevelStart);
    }

    private void BeginLevel(int level)
    {
        Level = level;
        LayOut();
        SetPhase(Phase.LevelStart);
        Say($"LEVEL {level}: {Castle.Name}", WitchLevel ? "THE WITCH IS HERE" : level == 1 ? "GATHER EVERY GEM" : null, StartSeconds);
        Play(WitchLevel ? Sfx.Witch : Sfx.LevelStart);
    }

    private void LayOut()
    {
        Castle = Castle.Build(Level);
        Effects.Clear();
        LevelTime = 0;
        _honeyDone = false;
        Honey = null;
        // The hat waits on a square well away from the start.
        var rng = new Random(Level * 13 + 1);
        var far = Castle.Cells().Where(c => Castle.Gems[c.I, c.J] && Distance(Castle.Start, c) >= 7).ToList();
        Hat = far.Count > 0 ? far[rng.Next(far.Count)] : null;
        PlaceEnemies(rng);
        Reset();
    }

    private static int Distance((int I, int J) a, (int I, int J) b) => Math.Abs(a.I - b.I) + Math.Abs(a.J - b.J);

    private void PlaceEnemies(Random rng)
    {
        Enemies.Clear();
        if (Calm) return;
        var far = Castle.Cells().Where(c => Castle.Squares[c.I, c.J].Walk && !Castle.Squares[c.I, c.J].Stair && Distance(Castle.Start, c) >= 6).ToList();
        void Add(EnemyKind kind)
        {
            if (far.Count == 0) return;
            var c = far[rng.Next(far.Count)];
            far.Remove(c);
            Enemies.Add(new Enemy { Kind = kind, X = c.I + 0.5, Y = c.J + 0.5, ToI = c.I, ToJ = c.J, HomeI = c.I, HomeJ = c.J });
        }
        int grubs = 2 + Math.Min(Level / 2, 2), orbs = 1 + Math.Min(Level / 2, 2), trees = Level >= 2 ? 1 + Math.Min(Level / 4, 1) : 0;
        for (int k = 0; k < grubs; k++) Add(EnemyKind.Grub);
        for (int k = 0; k < orbs; k++) Add(EnemyKind.Orb);
        for (int k = 0; k < trees; k++) Add(EnemyKind.Tree);
        if (WitchLevel) Add(EnemyKind.Witch);
    }

    /// <summary>The bear at the start, the enemies back home, the bees gone.</summary>
    private void Reset()
    {
        var b = Bear;
        b.X = Castle.Start.I + 0.5;
        b.Y = Castle.Start.J + 0.5;
        b.Lift = b.Vz = 0;
        b.Alive = true;
        b.Hat = 0;
        foreach (var e in Enemies) { e.X = e.HomeI + 0.5; e.Y = e.HomeJ + 0.5; e.ToI = e.HomeI; e.ToJ = e.HomeJ; e.Munch = 0; }
        Bees.Active = false;
        Bees.Age = 0;
        _grace = GraceSeconds;
    }

    /// <summary>After a lost bear the enemies come back at fresh squares well away from the start, so no two lives play alike.</summary>
    private void Scatter()
    {
        var far = Castle.Cells().Where(c => Castle.Squares[c.I, c.J].Walk && !Castle.Squares[c.I, c.J].Stair && Distance(Castle.Start, c) >= 6).ToList();
        foreach (var e in Enemies)
        {
            if (far.Count == 0) break;
            var c = far[_rng.Next(far.Count)];
            far.Remove(c);
            e.HomeI = c.I;
            e.HomeJ = c.J;
        }
    }

    /// <summary>A moment after a new bear appears before anything moves.</summary>
    public const double GraceSeconds = 1.6;
    private double _grace;

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.LevelStart:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                LevelTime += dt;
                MoveBear(c, dt);
                MoveEnemies(dt);
                Extras(dt);
                Touch();
                if (Phase == Phase.Play && Castle.GemCount == 0) LevelDone(false);
                break;
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Scatter();
                    Reset();
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.LevelClear:
                if (PhaseTime > ClearSeconds) BeginLevel(Level + 1);
                break;
        }
        Enemies.RemoveAll(e => e.Dead);
    }

    // ───────────────────────────── the bear ─────────────────────────────

    /// <summary>
    /// The way on the castle for a way of the stick. The diagonals run straight along the walkways (down and
    /// right is +X, up and right is −Y); straight up, down, left or right cut across them.
    /// </summary>
    public static (double X, double Y) WorldWay(int dx, int dy)
    {
        double wx = dx + dy, wy = dy - dx;
        double len = Math.Sqrt(wx * wx + wy * wy);
        return len < 1e-9 ? (0, 0) : (wx / len, wy / len);
    }

    private void MoveBear(Controls c, double dt)
    {
        var b = Bear;
        bool jump = c.Jump && !_jumpPrev;
        _jumpPrev = c.Jump;
        if (b.Hat > 0)
        {
            b.Hat -= dt;
            if (b.Hat <= 0) { b.Hat = 0; Play(Sfx.HatOff); }
        }
        // Jumping: up and down, carrying on the way it was going.
        if (jump && b.Lift <= 0) { b.Vz = JumpSpeed; b.Lift = 0.01; Play(Sfx.Jump); }
        if (b.Lift > 0)
        {
            b.Vz -= Gravity * dt;
            b.Lift += b.Vz * dt;
            if (b.Lift <= 0) { b.Lift = 0; b.Vz = 0; Play(Sfx.Land); }
        }
        var (wx, wy) = WorldWay(c.Dx, c.Dy);
        // Whatever it stands on, it picks up, moving or not.
        if (wx == 0 && wy == 0) { Gather(); return; }
        b.FaceX = c.Dx;
        b.FaceY = c.Dy;
        double step = BearSpeed * dt;
        // One way at a time, so it slides along a wall.
        TryMove(b, b.X + wx * step, b.Y);
        TryMove(b, b.X, b.Y + wy * step);
        b.Walk += step;
        Gather();
    }

    private void TryMove(Bear b, double nx, double ny)
    {
        int i = (int)Math.Floor(b.X), j = (int)Math.Floor(b.Y), ni = (int)Math.Floor(nx), nj = (int)Math.Floor(ny);
        if (ni == i && nj == j) { b.X = nx; b.Y = ny; return; }
        if (Castle.CanStep(i, j, ni, nj)) { b.X = nx; b.Y = ny; return; }
        // Blocked: up to the edge, no further.
        if (ni != i) b.X = ni > i ? i + 0.999 : i + 0.001;
        if (nj != j) b.Y = nj > j ? j + 0.999 : j + 0.001;
    }

    private void Gather()
    {
        var b = Bear;
        int i = (int)Math.Floor(b.X), j = (int)Math.Floor(b.Y);
        if (!Field.Inside(i, j)) return;
        if (Castle.Gems[i, j] && Math.Abs(b.X - (i + 0.5)) < 0.4 && Math.Abs(b.Y - (j + 0.5)) < 0.4 && b.Lift < 0.5)
        {
            Castle.Gems[i, j] = false;
            AddScore(GemPoints, i + 0.5, j + 0.5, quiet: true);
            Play(Sfx.Gem);
            if (Castle.GemCount == 0) LevelDone(true);
        }
        if (Hat == (i, j))
        {
            Hat = null;
            b.Hat = HatSeconds;
            Effects.Add(new Effect { Kind = EffectKind.Word, X = b.X, Y = b.Y, Z = 2, Text = "MAGIC HAT!", Colour = 0xFFB070FF, Life = 1.4 });
            Play(Sfx.Hat);
        }
        if (Honey == (i, j))
        {
            Honey = null;
            AddScore(HoneyPoints, b.X, b.Y);
            Play(Sfx.Honey);
        }
    }

    // ───────────────────────────── the enemies ─────────────────────────────

    public double SpeedOf(EnemyKind k) => (k switch { EnemyKind.Grub => 1.5, EnemyKind.Orb => 2.15, EnemyKind.Tree => 1.3, _ => 1.9 }) * Pace;

    private void MoveEnemies(double dt)
    {
        var b = Bear;
        int bi = (int)Math.Floor(b.X), bj = (int)Math.Floor(b.Y);
        if (_grace > 0) { _grace -= dt; return; }
        foreach (var e in Enemies)
        {
            e.Age += dt;
            if (e.Munch > 0)
            {
                e.Munch -= dt;
                if (e.Munch <= 0 && Castle.Gems[e.ToI, e.ToJ])
                {
                    // Swallowed: that gem is gone for good.
                    Castle.Gems[e.ToI, e.ToJ] = false;
                    Play(Sfx.Munch);
                }
                continue;
            }
            double cx = e.ToI + 0.5, cy = e.ToJ + 0.5, dx = cx - e.X, dy = cy - e.Y, d = Math.Sqrt(dx * dx + dy * dy);
            double step = SpeedOf(e.Kind) * dt;
            if (d > step) { e.X += dx / d * step; e.Y += dy / d * step; continue; }
            e.X = cx;
            e.Y = cy;
            // At a square's centre: a grub stops to eat a gem here; then everyone picks the next square.
            if (e.Kind == EnemyKind.Grub && Castle.Gems[e.ToI, e.ToJ] && e.Age > 1) { e.Munch = MunchSeconds; continue; }
            var way = e.Kind == EnemyKind.Grub
                ? Castle.Toward(e.ToI, e.ToJ, (i, j) => Castle.Gems[i, j])
                : Castle.Toward(e.ToI, e.ToJ, (i, j) => i == bi && j == bj);
            (int X, int Y) next;
            if (way is { } w && _rng.NextDouble() > 0.12) next = w.Step;
            else
            {
                var open = Castle.Ways.Where(o => Castle.CanStep(e.ToI, e.ToJ, e.ToI + o.X, e.ToJ + o.Y)).ToList();
                if (open.Count == 0) continue;
                next = open[_rng.Next(open.Count)];
            }
            e.ToI += next.X;
            e.ToJ += next.Y;
        }
    }

    private void Extras(double dt)
    {
        if (Calm) return;
        // The honey pot, once a level, for a while.
        if (!_honeyDone && LevelTime > HoneyAt)
        {
            _honeyDone = true;
            var spots = Castle.Cells().Where(c => Castle.Squares[c.I, c.J].Walk && !Castle.Squares[c.I, c.J].Stair && !Castle.Gems[c.I, c.J]).ToList();
            if (spots.Count > 0) { Honey = spots[_rng.Next(spots.Count)]; _honeyLeft = HoneySeconds; }
        }
        if (Honey != null && (_honeyLeft -= dt) <= 0) Honey = null;
        // The bees come out for a while, flying straight at the bear over everything.
        var s = Bees;
        if (!s.Active && LevelTime > BeesAt && LevelTime < BeesAt + 0.1)
        {
            s.Active = true;
            s.Age = 0;
            s.X = 0.5;
            s.Y = Field.Size / 2.0;
            Play(Sfx.Bees);
        }
        if (s.Active)
        {
            s.Age += dt;
            double dx = Bear.X - s.X, dy = Bear.Y - s.Y, d = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
            s.X += dx / d * BeeSpeed * Pace * dt + Math.Sin(s.Age * 9) * 0.6 * dt;
            s.Y += dy / d * BeeSpeed * Pace * dt + Math.Cos(s.Age * 7) * 0.6 * dt;
            if (s.Age > BeesSeconds) s.Active = false;
        }
    }

    private double _honeyLeft;

    private void Touch()
    {
        var b = Bear;
        double bz = Castle.Z(b.X, b.Y);
        foreach (var e in Enemies)
        {
            double dx = e.X - b.X, dy = e.Y - b.Y;
            if (dx * dx + dy * dy > TouchRange * TouchRange || Math.Abs(Castle.Z(e.X, e.Y) - bz) > 0.6) continue;
            // The hat squashes anything, and a grub can be squashed while it eats.
            if (b.Hat > 0 || (e.Kind == EnemyKind.Grub && e.Munch > 0))
            {
                e.Dead = true;
                AddScore(e.Points, e.X, e.Y);
                Play(Sfx.Squash);
                continue;
            }
            if (b.Airborne) continue;
            Caught(e.Kind switch { EnemyKind.Grub => "CAUGHT BY A GEM GRUB", EnemyKind.Orb => "FLATTENED BY AN ORB", EnemyKind.Tree => "TANGLED IN A THORN TREE", _ => "CAUGHT BY THE WITCH" });
            return;
        }
        if (Bees.Active && Math.Abs(Bees.X - b.X) < 0.5 && Math.Abs(Bees.Y - b.Y) < 0.5)
        {
            if (b.Hat > 0)
            {
                Bees.Active = false;
                AddScore(BeePoints, Bees.X, Bees.Y);
                Play(Sfx.Squash);
            }
            else Caught("STUNG BY THE BEES");
        }
    }

    private void Caught(string cause)
    {
        if (Phase != Phase.Play) return;
        Cause = cause;
        Lives--;
        Bear.Alive = false;
        Play(Sfx.Death);
        Music(false);
        SetPhase(Phase.Dying);
        Say(cause, Lives > 0 ? $"{Lives} {(Lives == 1 ? "BEAR" : "BEARS")} LEFT" : null, DyingSeconds);
    }

    private void LevelDone(bool lastByBear)
    {
        int bonus = 1000 * Level, last = lastByBear ? 500 * Level : 0;
        AddScore(bonus + last, Bear.X, Bear.Y);
        Say(lastByBear ? "THE LAST GEM!" : "THE CASTLE IS CLEARED", $"BONUS {bonus}" + (last > 0 ? $" + LAST GEM {last}" : ""), ClearSeconds);
        Play(lastByBear ? Sfx.LastGem : Sfx.LevelClear);
        Music(false);
        SetPhase(Phase.LevelClear);
    }

    private void AddScore(int points, double x, double y, bool quiet = false)
    {
        Score += points;
        if (!quiet) Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Z = Castle.Z(x, y) + 1.5, Text = points.ToString(), Colour = points >= 1000 ? 0xFFFFE14A : 0xFFF2F2F2 });
        else Effects.Add(new Effect { Kind = EffectKind.Sparkle, X = x, Y = y, Z = Castle.Z(x, y), Life = 0.4 });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a level.</summary>
    internal void Go(int level = 1)
    {
        StartGame();
        BeginLevel(level);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal Enemy PutEnemy(EnemyKind kind, int i, int j)
    {
        var e = new Enemy { Kind = kind, X = i + 0.5, Y = j + 0.5, ToI = i, ToJ = j, HomeI = i, HomeJ = j, Age = 2 };
        Enemies.Add(e);
        return e;
    }

    internal void PutBear(double x, double y)
    {
        Bear.X = x;
        Bear.Y = y;
    }

    internal void SetHat((int I, int J)? at) => Hat = at;
    internal void NoGrace() => _grace = 0;
    internal void SetHoney((int I, int J)? at) { Honey = at; _honeyLeft = HoneySeconds; }
    internal void SetLevelTime(double t) => LevelTime = t;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void ClearEnemies() => Enemies.Clear();
}
